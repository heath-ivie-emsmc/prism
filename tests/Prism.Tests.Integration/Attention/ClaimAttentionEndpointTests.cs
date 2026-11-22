using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Prism.Application.Attention;
using Prism.Application.Audit;
using Prism.Application.Auth;
using Prism.Application.Metrics;
using Prism.Application.Warehouse;
using Prism.Tests.Integration.Auth;

namespace Prism.Tests.Integration.Attention;

/// <summary>The claims-needing-attention routes: the permission, the Provider scope, the audit record, the PHI in the body, the CSV and the bounds.</summary>
[Collection(LogCaptureGroup.Name)]
public sealed class ClaimAttentionEndpointTests : IDisposable
{
    private const string Base = "/api/v1/providers/100/claims/attention";
    private const string Marker = "=cmd|' /C calc'!A0";

    private readonly FakeClaims claims = new();
    private readonly AuthTestFactory factory;

    public ClaimAttentionEndpointTests()
    {
        factory = new AuthTestFactory(configureServices: services =>
        {
            services.RemoveAll<IClaimAttention>();
            services.AddSingleton<IClaimAttention>(claims);
            services.RemoveAll<IProviderDivisionDirectory>();
            services.AddSingleton<IProviderDivisionDirectory>(new FakeDivisions());
        });
        // Provider 100 in full, 200 limited to one Facility.
        factory.Directory.Providers.Clear();
        factory.Directory.Providers.AddRange([new ProviderScope("100", "Mercy Air", []), new ProviderScope("200", "Limited", ["9"])]);
    }

    public void Dispose() => factory.Dispose();

    private async Task<(HttpStatusCode Status, string Text, HttpResponseMessage Response)> GetAsync(string url, params string[] permissions)
    {
        using CookieClient client = factory.NewSignedInClient(permissions);
        HttpResponseMessage response = await client.GetAsync(url);
        return (response.StatusCode, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), response);
    }

    private static readonly string[] WithClaims = [Permissions.App, Permissions.DashboardView, Permissions.ClaimsView];
    private static readonly string[] WithoutClaims = [Permissions.App, Permissions.DashboardView, Permissions.ViewsManage];

    [Fact]
    public async Task A_user_with_the_claims_permission_gets_the_rows_and_one_audit_record()
    {
        (HttpStatusCode status, string text, _) = await GetAsync($"{Base}/denied?period=202610", WithClaims);

        Assert.Equal(HttpStatusCode.OK, status);
        JsonElement body = JsonElement.Parse(text);
        Assert.Equal(2, body.GetProperty("total").GetInt32());
        JsonElement first = body.GetProperty("claims")[0];
        Assert.Equal("FW-2610418", first.GetProperty("claimRef").GetString());
        Assert.Equal("Missing documentation", first.GetProperty("denialReason").GetString());
        Assert.Equal(24600m, first.GetProperty("balanceDue").GetDecimal());
        Assert.Equal(21, first.GetProperty("daysLeft").GetInt32());
        AuditCall call = Assert.Single(factory.Audit.Calls);
        Assert.Equal(new AuditCall(100, 7, AuditActions.ClaimsList, "denied", "202610", 2), call);
    }

    [Fact]
    public async Task Without_the_claims_permission_every_route_is_refused_and_nothing_is_read_or_audited()
    {
        foreach (string url in new[] { $"{Base}/counts?period=202610", $"{Base}/denied?period=202610", $"{Base}/denied/export?period=202610" })
        {
            (HttpStatusCode status, string text, _) = await GetAsync(url, WithoutClaims);

            Assert.Equal(HttpStatusCode.Forbidden, status);
            Assert.DoesNotContain("FW-2610418", text, StringComparison.Ordinal);
            Assert.DoesNotContain("Meridian", text, StringComparison.Ordinal);
        }

        Assert.Equal(0, claims.Reads);
        Assert.Empty(factory.Audit.Calls);
    }

    [Fact]
    public async Task Another_providers_claims_are_not_found_and_nothing_is_read()
    {
        (HttpStatusCode status, _, _) = await GetAsync("/api/v1/providers/999/claims/attention/denied?period=202610", WithClaims);

        Assert.Equal(HttpStatusCode.NotFound, status);
        Assert.Equal(0, claims.Reads);
        Assert.Empty(factory.Audit.Calls);
    }

    [Fact]
    public async Task A_facility_limited_user_is_refused_on_all_three_routes()
    {
        foreach (string url in new[] { "counts?period=202610", "denied?period=202610", "denied/export?period=202610" })
        {
            (HttpStatusCode status, _, _) = await GetAsync($"/api/v1/providers/200/claims/attention/{url}", WithClaims);

            Assert.Equal(HttpStatusCode.NotFound, status);
        }

        Assert.Equal(0, claims.Reads);
    }

    [Fact]
    public async Task Counts_are_aggregates_with_no_claim_detail()
    {
        (HttpStatusCode status, string text, _) = await GetAsync($"{Base}/counts?period=202610&serviceLine=air", WithClaims);

        Assert.Equal(HttpStatusCode.OK, status);
        JsonElement body = JsonElement.Parse(text);
        Assert.Equal(["denied", "needs_attention", "all"], body.GetProperty("counts").EnumerateArray().Select(c => c.GetProperty("key").GetString()));
        Assert.Equal(48200m, body.GetProperty("deniedBalanceDue").GetDecimal());
        Assert.DoesNotContain("FW-2610418", text, StringComparison.Ordinal);
        Assert.Equal(ServiceLineFilter.Air, claims.LastServiceLine);
        Assert.Empty(factory.Audit.Calls);
    }

    [Fact]
    public async Task An_aircraft_type_is_passed_through_to_the_read()
    {
        (HttpStatusCode status, _, _) = await GetAsync($"{Base}/denied?period=202610&group=fixed-wing", WithClaims);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("fixed-wing", claims.LastGroup);
    }

    [Theory]
    [InlineData("")]
    [InlineData("?period=2026")]
    [InlineData("?period=202613")]
    [InlineData("?period=202610&serviceLine=both")]
    [InlineData("?period=202610&group=Fixed%20Wing;DROP")]
    [InlineData("?period=202610&pageSize=0")]
    [InlineData("?period=202610&pageSize=101")]
    [InlineData("?period=202610&page=0")]
    [InlineData("?period=202610&page=1001")]
    public async Task Bad_input_is_a_400_and_reads_and_audits_nothing(string query)
    {
        (HttpStatusCode status, _, _) = await GetAsync($"{Base}/denied{query}", WithClaims);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal(0, claims.Reads);
        Assert.Empty(factory.Audit.Calls);
    }

    [Theory]
    [InlineData("DENIED")]
    [InlineData("denied;DROP")]
    [InlineData("1")]
    [InlineData("everything")]
    public async Task A_check_that_is_not_in_the_catalog_is_not_found(string key)
    {
        (HttpStatusCode status, _, _) = await GetAsync($"{Base}/{key}?period=202610", WithClaims);

        Assert.Equal(HttpStatusCode.NotFound, status);
        Assert.Equal(0, claims.Reads);
    }

    [Fact]
    public async Task Paging_returns_the_page_asked_for_and_the_total()
    {
        claims.Rows = Enumerable.Range(1, 30).Select(i => Claim($"C-{i}", i)).ToList();

        (_, string text, _) = await GetAsync($"{Base}/all?period=202610&page=2&pageSize=25", WithClaims);

        JsonElement body = JsonElement.Parse(text);
        Assert.Equal(30, body.GetProperty("total").GetInt32());
        Assert.Equal(5, body.GetProperty("claims").GetArrayLength());
        Assert.Equal("C-26", body.GetProperty("claims")[0].GetProperty("claimRef").GetString());
        Assert.Equal(5, factory.Audit.Calls.Single().RowCount);
    }

    [Fact]
    public async Task Fields_that_try_to_set_the_provider_or_the_audit_are_ignored()
    {
        (HttpStatusCode status, _, _) = await GetAsync($"{Base}/denied?period=202610&providerId=999&portalUserId=1&action=none&checkKey=all", WithClaims);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(new AuditCall(100, 7, AuditActions.ClaimsList, "denied", "202610", 2), Assert.Single(factory.Audit.Calls));
        Assert.Equal(100, claims.LastProviderId);
    }

    [Fact]
    public async Task More_claims_than_the_cap_is_a_calm_422_not_a_500()
    {
        claims.Failure = new WarehouseResultTooLargeException(10_000);

        (HttpStatusCode status, string text, _) = await GetAsync($"{Base}/all?period=202610", WithClaims);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, status);
        Assert.Contains("Narrow the period", text, StringComparison.Ordinal);
        Assert.Empty(factory.Audit.Calls);
    }

    [Fact]
    public async Task A_failed_audit_write_fails_the_request_and_returns_no_claims()
    {
        factory.Audit.Failure = new InvalidOperationException("database down");

        (HttpStatusCode status, string text, _) = await GetAsync($"{Base}/denied?period=202610", WithClaims);

        Assert.Equal(HttpStatusCode.InternalServerError, status);
        Assert.DoesNotContain("FW-2610418", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_export_is_a_csv_with_every_cell_quoted_formulas_neutralized_and_a_name_with_no_claim_data()
    {
        claims.Rows = [Claim(Marker, 3, denialReason: "Said \"no\"")];

        (HttpStatusCode status, string text, HttpResponseMessage response) = await GetAsync($"{Base}/denied/export?period=202610", WithClaims);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);
        string fileName = response.Content.Headers.ContentDisposition?.FileName ?? "";
        Assert.Matches(@"^prism-needs-attention-denied-\d{4}-\d{2}-\d{2}\.csv$", fileName);
        string[] lines = text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        Assert.StartsWith("\"Claim\",\"Service date\"", lines[0], StringComparison.Ordinal);
        Assert.StartsWith("\"'=cmd|' /C calc'!A0\",", lines[1], StringComparison.Ordinal);
        Assert.Contains("\"Said \"\"no\"\"\"", lines[1], StringComparison.Ordinal);
        Assert.Equal(new AuditCall(100, 7, AuditActions.ClaimsExport, "denied", "202610", 1), Assert.Single(factory.Audit.Calls));
    }

    [Fact]
    public async Task An_overdue_claim_reads_overdue_in_the_export_instead_of_a_negative_number()
    {
        claims.Rows = [Claim("C-1", -5)];

        (_, string text, _) = await GetAsync($"{Base}/denied/export?period=202610", WithClaims);

        Assert.Contains("\"Overdue by 5\"", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_failed_warehouse_is_a_503_with_no_claim_data()
    {
        claims.Failure = new WarehouseUnavailableException();

        (HttpStatusCode status, string text, _) = await GetAsync($"{Base}/denied?period=202610", WithClaims);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, status);
        Assert.DoesNotContain("FW-2610418", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_marker_in_the_query_never_reaches_the_response_or_the_logs()
    {
        // Logs carry the request path (a code and a length, not values), never the query string.
        (HttpStatusCode status, string text, _) = await GetAsync($"{Base}/denied?period=PHI-MARKER&serviceLine=PHI-MARKER&page=PHI-MARKER", WithClaims);
        await Task.Delay(200, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.DoesNotContain("PHI-MARKER", text, StringComparison.Ordinal);
        Assert.DoesNotContain(factory.Logs.Entries, entry => entry.Text.Contains("PHI-MARKER", StringComparison.Ordinal));
    }

    private static AttentionClaim Claim(string reference, int daysLeft, string? denialReason = "Missing documentation") => new(
        1, reference, new DateOnly(2026, 10, 2), "Meridian Health Plan", "Insurance", "N418CV", "Sky Harbor", "Submitted", "CO-16", denialReason, 100m,
        new DateOnly(2026, 11, 26), daysLeft);

    private sealed class FakeDivisions : IProviderDivisionDirectory
    {
        public Task<IReadOnlySet<string>> GetFacilityIdsAsync(int portalCompanyId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlySet<string>>(portalCompanyId == 200 ? new HashSet<string> { "9", "10" } : []);
    }

    private sealed class FakeClaims : IClaimAttention
    {
        public int Reads { get; private set; }

        public Exception? Failure { get; set; }

        public int LastProviderId { get; private set; }

        public ServiceLineFilter LastServiceLine { get; private set; }

        public string? LastGroup { get; private set; }

        public List<AttentionClaim> Rows { get; set; } =
        [
            Claim("FW-2610418", 21) with { BalanceDue = 24600m, FilingDueDate = new DateOnly(2026, 11, 26) },
            Claim("FW-2610457", 24) with { BalanceDue = 23600m, FilingDueDate = new DateOnly(2026, 11, 29) },
        ];

        public Task<AttentionCounts> GetCountsAsync(int providerId, ApPeriod period, ServiceLineFilter serviceLine, string? groupKey, CancellationToken cancellationToken)
        {
            Reads++;
            LastProviderId = providerId;
            LastServiceLine = serviceLine;
            LastGroup = groupKey;
            return Task.FromResult(new AttentionCounts(
                new DateOnly(2026, 11, 5), [new AttentionCount("denied", 2), new AttentionCount("needs_attention", 4), new AttentionCount("all", 8)], 48200m));
        }

        public Task<AttentionClaims> GetClaimsAsync(int providerId, AttentionCheck check, ApPeriod period, ServiceLineFilter serviceLine, string? groupKey, CancellationToken cancellationToken)
        {
            Reads++;
            LastProviderId = providerId;
            LastServiceLine = serviceLine;
            LastGroup = groupKey;
            return Failure is { } failure ? throw failure : Task.FromResult(new AttentionClaims(new DateOnly(2026, 11, 5), Rows));
        }
    }
}
