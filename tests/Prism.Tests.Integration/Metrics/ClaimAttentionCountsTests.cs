using Prism.Testing.Common;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Prism.Application.Attention;
using Prism.Application.Metrics;
using Prism.Infrastructure.Metrics;
using Prism.Infrastructure.Warehouse;
using Prism.Tests.Integration.Persistence;

namespace Prism.Tests.Integration.Metrics;

/// <summary>
/// The extra counts behind the attention page (denied rate, denial reasons, categories, filing bands) against a real PostgreSQL holding a table shaped like
/// <c>reporting.trip_detail_snapshot</c>. Each test seeds the rows it needs.
/// </summary>
public sealed class ClaimAttentionCountsTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static readonly SemaphoreSlim SeedLock = new(1, 1);
    private static readonly ApPeriod October = new(2026, 10);

    /// <summary>One seeded claim. Data Through is 2026-11-21 unless <paramref name="Snapshot"/> says otherwise. The default date of service is 2026-10-15.</summary>
    private sealed record Seed(
        long Id,
        string Dos = "2026-10-15",
        string Status = "Submitted",
        string InCollections = "No",
        string LevelOfService = "Fixed Wing",
        string Category = "Insurance",
        string? Code = null,
        string? Reason = null,
        decimal Balance = 0m,
        int Provider = 2026,
        string Snapshot = "2026-11-21");

    private async Task<ClaimAttentionReader> NewReaderAsync(IEnumerable<Seed> rows, FilingLimitOptions? limits = null)
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await SeedLock.WaitAsync(ct);
        try
        {
            await using var admin = new NpgsqlConnection(TestDatabaseGuard.Assert(postgres.ConnectionString));
            await admin.OpenAsync(ct);
            await using (var create = new NpgsqlCommand(
                """
                CREATE SCHEMA IF NOT EXISTS reporting;
                DROP TABLE IF EXISTS reporting.trip_detail_snapshot;
                CREATE TABLE reporting.trip_detail_snapshot (
                    snapshot_date date NOT NULL, encounter_id bigint NOT NULL, provider_id int NOT NULL, run_number text, dos date NOT NULL,
                    status text NOT NULL, in_collections text NOT NULL, level_of_service text, prim_pay text, prim_pay_category text, vehicle text,
                    reporting_division text NOT NULL, denial_code text, denial_reason text, balance_due numeric NOT NULL);
                """,
                admin))
            {
                await create.ExecuteNonQueryAsync(ct);
            }

            foreach (Seed row in rows)
            {
                await using var insert = new NpgsqlCommand(
                    """
                    INSERT INTO reporting.trip_detail_snapshot VALUES
                        (@snapshot::date, @id, @provider, 'R', @dos::date, @status, @coll, @los, 'Payer', @category, 'V', 'Division', @code, @reason, @balance)
                    """,
                    admin);
                insert.Parameters.AddWithValue("snapshot", row.Snapshot);
                insert.Parameters.AddWithValue("id", row.Id);
                insert.Parameters.AddWithValue("provider", row.Provider);
                insert.Parameters.AddWithValue("dos", row.Dos);
                insert.Parameters.AddWithValue("status", row.Status);
                insert.Parameters.AddWithValue("coll", row.InCollections);
                insert.Parameters.AddWithValue("los", row.LevelOfService);
                insert.Parameters.AddWithValue("category", row.Category);
                insert.Parameters.AddWithValue("code", (object?)row.Code ?? DBNull.Value);
                insert.Parameters.AddWithValue("reason", (object?)row.Reason ?? DBNull.Value);
                insert.Parameters.AddWithValue("balance", row.Balance);
                await insert.ExecuteNonQueryAsync(ct);
            }
        }
        finally
        {
            SeedLock.Release();
        }

        var options = new PrismWarehouseOptions { ConnectionString = postgres.ConnectionString };
        var warehouse = new NpgsqlWarehouse(WarehouseDataSourceFactory.Create(options), Options.Create(options), NullLogger<NpgsqlWarehouse>.Instance);
        return new ClaimAttentionReader(warehouse, new FakeProviderResolver(null), Options.Create(limits ?? new FilingLimitOptions { Default = 365 }));
    }

    private static Task<AttentionCounts> CountsAsync(ClaimAttentionReader reader) =>
        reader.GetCountsAsync(2026, October, ServiceLineFilter.Both, null, TestContext.Current.CancellationToken);

    private static Seed Denied(long id, string? reason, decimal balance = 100m) => new(id, Code: "CO-16", Reason: reason, Balance: balance);

    [Fact]
    public async Task The_denied_rate_is_denied_claims_over_all_claims_in_the_period()
    {
        // Three denied (a code and money owed) of eight claims. The September claim, another Provider's and an old load are not in the period.
        ClaimAttentionReader reader = await NewReaderAsync(
        [
            Denied(1, "a"), Denied(2, "a"), Denied(3, "b"),
            new(4, Code: "CO-45", Balance: 0m), new(5), new(6), new(7), new(8),
            new(9, Dos: "2026-09-30", Code: "CO-16", Balance: 5m), new(10, Provider: 777, Code: "CO-16", Balance: 5m),
        ]);

        AttentionCounts counts = await CountsAsync(reader);

        Assert.Equal(0.375m, counts.DeniedRate);
        Assert.Equal(3, counts.Counts.Single(c => c.Key == "denied").Count);
        Assert.Equal(8, counts.Counts.Single(c => c.Key == "all").Count);
    }

    [Fact]
    public async Task The_denied_rate_is_rounded_to_four_decimals_zero_with_no_denials_and_null_without_claims()
    {
        ClaimAttentionReader oneInThree = await NewReaderAsync([Denied(1, "a"), new(2), new(3)]);
        Assert.Equal(0.3333m, (await CountsAsync(oneInThree)).DeniedRate);

        ClaimAttentionReader none = await NewReaderAsync([new(1), new(2)]);
        Assert.Equal(0m, (await CountsAsync(none)).DeniedRate);

        ClaimAttentionReader empty = await NewReaderAsync([]);
        AttentionCounts counts = await CountsAsync(empty);
        Assert.Null(counts.DeniedRate);
        Assert.Null(counts.DataThrough);
        Assert.All(counts.Reasons!, _ => Assert.Fail("no reasons expected"));
        Assert.Equal(["past_due", "due_within_10_days", "due_within_30_days", "due_later"], counts.Bands!.Select(b => b.Key));
        Assert.All(counts.Bands!, b => Assert.Equal((0, 0m), (b.Claims, b.BalanceDue)));
    }

    [Fact]
    public async Task Reasons_are_the_top_ten_by_denied_claim_count_with_everything_else_in_one_other_row()
    {
        // Reason R01 has 12 claims, R02 has 11, and so on down to R12 with 1. The top 10 are R01 to R10 (12 down to 3). Other is R11 (2) and R12 (1).
        var rows = new List<Seed>();
        long id = 1;
        for (int reason = 1; reason <= 12; reason++)
        {
            for (int n = 0; n < 13 - reason; n++)
            {
                rows.Add(Denied(id++, $"R{reason:00}", 10m));
            }
        }

        // Claims that are not denied (no money owed, or no code) and another Provider's never count.
        rows.Add(new(id++, Code: "CO-16", Reason: "R12", Balance: 0m));
        rows.Add(new(id++, Reason: "R12", Balance: 50m));
        rows.Add(new(id, Provider: 777, Code: "CO-16", Reason: "R12", Balance: 50m));
        ClaimAttentionReader reader = await NewReaderAsync(rows);

        AttentionCounts counts = await CountsAsync(reader);

        IReadOnlyList<DenialReasonCount> reasons = counts.Reasons!;
        Assert.Equal(11, reasons.Count);
        Assert.Equal(Enumerable.Range(1, 10).Select(i => $"R{i:00}").Append("Other"), reasons.Select(r => r.Reason));
        Assert.Equal(Enumerable.Range(1, 10).Select(i => 13 - i).Append(3), reasons.Select(r => r.Count));
        Assert.Equal([false, false, false, false, false, false, false, false, false, false, true], reasons.Select(r => r.IsOther));
        Assert.Equal(30m, reasons[^1].BalanceDue);
        Assert.Equal(120m, reasons[0].BalanceDue);
        Assert.Equal(counts.Counts.Single(c => c.Key == "denied").Count, reasons.Sum(r => r.Count));
        Assert.Equal(counts.DeniedBalanceDue, reasons.Sum(r => r.BalanceDue));
    }

    [Fact]
    public async Task With_ten_or_fewer_reasons_there_is_no_other_row_and_ties_are_ordered_by_balance_then_text()
    {
        ClaimAttentionReader reader = await NewReaderAsync([Denied(1, "beta", 5m), Denied(2, "alpha", 5m), Denied(3, "gamma", 50m)]);

        AttentionCounts counts = await CountsAsync(reader);

        Assert.Equal(["gamma", "alpha", "beta"], counts.Reasons!.Select(r => r.Reason));
        Assert.DoesNotContain(counts.Reasons!, r => r.IsOther);
    }

    [Fact]
    public async Task Hostile_reason_text_is_cleaned_cut_and_never_empty()
    {
        string longReason = new('x', 500);
        ClaimAttentionReader reader = await NewReaderAsync(
        [
            Denied(1, "=cmd|' /C calc'!A0"),
            Denied(2, longReason),
            Denied(3, "  Missing \t\r\n  documentation \u0001\u001f  "),
            Denied(4, null),
            Denied(5, ""),
            Denied(6, " \t\n "),
            Denied(7, "Other"),
            Denied(8, "<script>alert(1)</script>"),
            Denied(9, "\"quoted\", with comma"),
            Denied(10, new string(' ', 119) + "y" + new string('z', 40)),
        ]);

        AttentionCounts counts = await CountsAsync(reader);

        string[] text = counts.Reasons!.Select(r => r.Reason).ToArray();
        Assert.Contains("=cmd|' /C calc'!A0", text);
        Assert.Contains(new string('x', 120), text);
        Assert.DoesNotContain(text, t => t.Length > AttentionCounts.ReasonMaxLength);
        Assert.Contains("Missing documentation", text);
        Assert.Equal(3, counts.Reasons!.Single(r => r.Reason == "No reason given").Count);
        Assert.DoesNotContain(text, t => t.Any(char.IsControl) || t.Contains("  ", StringComparison.Ordinal) || t != t.Trim());
        // A real reason called Other is renamed, so it cannot be mistaken for the summary row, which only exists with more than ten reasons.
        Assert.Contains("Other reason", text);
        Assert.DoesNotContain(counts.Reasons!, r => r.IsOther);
        // Markup and quotes stay literal text for the client to escape.
        Assert.Contains("<script>alert(1)</script>", text);
        Assert.Contains("\"quoted\", with comma", text);
        // Leading whitespace is dropped before the cut, so the 119 spaces do not eat the budget.
        Assert.Contains("y" + new string('z', 40), text);
    }

    [Fact]
    public async Task Categories_count_denied_on_hold_and_in_collections_separately_and_they_overlap()
    {
        ClaimAttentionReader reader = await NewReaderAsync(
        [
            Denied(1, "a", 100m),
            new(2, Status: "On_Hold", Balance: 40m),
            new(3, Status: "On_Hold", InCollections: "Yes", Balance: 60m),
            new(4, InCollections: "Yes", Balance: -25m),
            new(5, Code: "CO-16", Status: "On_Hold", Balance: 30m),
            new(6),
        ]);

        AttentionCounts counts = await CountsAsync(reader);

        // Claim 3 is on hold and in collections, so it is in both. Claim 5 is on hold but has a denial code with money owed, so it is denied too (denied balance 130).
        Assert.Equal(
            [("denied", 2, 130m), ("on_hold", 3, 130m), ("in_collections", 2, 60m)],
            counts.Categories!.Select(c => (c.Key, c.Count, c.BalanceDue)));
        Assert.Equal(5, counts.Counts.Single(c => c.Key == "needs_attention").Count);
    }

    /// <summary>Claims on hold with a date of service of 2026-10-15 and a filing limit that puts the due date exactly <paramref name="daysLeft"/> days after Data Through (2026-11-21).</summary>
    private static (Seed Claim, string Category, int Limit) AtDaysLeft(long id, int daysLeft, decimal balance) =>
        (new Seed(id, Status: "On_Hold", Category: $"L{id}", Balance: balance), $"L{id}", 37 + daysLeft);

    [Fact]
    public async Task Filing_bands_split_at_exactly_ten_and_thirty_days_and_due_today_is_not_past_due()
    {
        // 2026-10-15 plus a limit of (37 + d) days is d days after 2026-11-21.
        (Seed Claim, string Category, int Limit)[] cases =
        [
            AtDaysLeft(1, -1, 1m), AtDaysLeft(2, 0, 2m), AtDaysLeft(3, 10, 4m), AtDaysLeft(4, 11, 8m),
            AtDaysLeft(5, 30, 16m), AtDaysLeft(6, 31, 32m), AtDaysLeft(7, -30, 64m),
        ];
        var limits = new FilingLimitOptions { Default = 365, ByPayerCategory = cases.ToDictionary(c => c.Category, c => c.Limit) };
        ClaimAttentionReader reader = await NewReaderAsync(
            cases.Select(c => c.Claim)
                .Concat([new Seed(20), new Seed(21, Dos: "2026-09-30", Status: "On_Hold", Balance: 999m), new Seed(22, Provider: 777, Status: "On_Hold", Balance: 999m), new Seed(23, Snapshot: "2026-10-01", Status: "On_Hold", Balance: 999m)]),
            limits);

        AttentionCounts counts = await CountsAsync(reader);

        // Past due: -1 and -90. Within 10 days: 0 and 10. 11 to 30: 11 and 30. Later: 31. Claim 20 is not flagged. 21 is September, 22 another Provider, 23 an old load.
        Assert.Equal(
            [("past_due", 2, 65m), ("due_within_10_days", 2, 6m), ("due_within_30_days", 2, 24m), ("due_later", 1, 32m)],
            counts.Bands!.Select(b => (b.Key, b.Claims, b.BalanceDue)));
        Assert.Equal(7, counts.Counts.Single(c => c.Key == "needs_attention").Count);
    }

    [Fact]
    public async Task Filing_bands_use_the_payer_category_limit_the_default_and_the_denied_claims_too()
    {
        var limits = new FilingLimitOptions { Default = 40, ByPayerCategory = new() { ["Medicare"] = 20 } };
        // Date of service 2026-10-15: default 40 days is due 2026-11-24 (3 days left), Medicare 20 days is due 2026-11-04 (past due).
        ClaimAttentionReader reader = await NewReaderAsync(
            [Denied(1, "a", 70m), new(2, Category: "Medicare", Status: "On_Hold", Balance: 5m), new(3, Category: "medicare", InCollections: "Yes", Balance: -5m)],
            limits);

        AttentionCounts counts = await CountsAsync(reader);

        Assert.Equal(
            [("past_due", 2, 5m), ("due_within_10_days", 1, 70m), ("due_within_30_days", 0, 0m), ("due_later", 0, 0m)],
            counts.Bands!.Select(b => (b.Key, b.Claims, b.BalanceDue)));
    }

    [Fact]
    public async Task The_extra_counts_follow_the_service_line_and_aircraft_type_filters()
    {
        ClaimAttentionReader reader = await NewReaderAsync(
        [
            Denied(1, "air reason"), new(2, Code: "CO-16", Reason: "ground reason", Balance: 9m, LevelOfService: "ALS"),
            new(3, Status: "On_Hold", LevelOfService: "Rotor Wing"),
        ]);
        CancellationToken ct = TestContext.Current.CancellationToken;

        AttentionCounts ground = await reader.GetCountsAsync(2026, October, ServiceLineFilter.Ground, null, ct);
        AttentionCounts fixedWing = await reader.GetCountsAsync(2026, October, ServiceLineFilter.Air, "fixed-wing", ct);

        Assert.Equal(["ground reason"], ground.Reasons!.Select(r => r.Reason));
        Assert.Equal(["air reason"], fixedWing.Reasons!.Select(r => r.Reason));
        Assert.Equal(0, fixedWing.Categories!.Single(c => c.Key == "on_hold").Count);
        Assert.Equal(1, fixedWing.Bands!.Sum(b => b.Claims));
    }
}
