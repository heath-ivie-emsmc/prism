using Prism.Testing.Common;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Prism.Application.Warehouse;
using Prism.Infrastructure.Warehouse;
using Prism.Tests.Integration.Persistence;

namespace Prism.Tests.Integration.Metrics;

/// <summary>One trip row of the seeded <c>reporting.trip_detail_snapshot</c>. Every row also carries PHI markers in the origin address columns, so a Metric that selected them would show it.</summary>
internal sealed record SeedTrip(
    long Encounter,
    string? Los,
    string? ResponseType,
    string? Pay,
    string? Category,
    string Dos,
    decimal? Miles,
    string Division,
    string? Vehicle = null,
    string? Facility = null,
    string? Mod = null,
    int Provider = 2026,
    string Load = "2026-11-21");

/// <summary>One credit transaction (a payment, type 2) of the seeded ledger. <c>Period</c> is the accounting period id: 1 is 202610, 2 is 202609, 3 is the 202610 adjustment, 4 is 202611, 9 is another Provider's.</summary>
internal sealed record SeedCash(long Id, long Encounter, int Period, int Cents, string Source = "Posting");

/// <summary>
/// A real PostgreSQL shaped like the warehouse tables the fleet, call type, payer effect and pickup Metrics read: the trip table (with the pickup and PHI origin columns)
/// and the ledger. Each test class owns its own container, so a seed here never meets another class.
/// </summary>
internal static class FleetSeed
{
    internal const string Rotor = "Mercy Flights - Rotor";
    internal const string FixedLegacy = "Mercy Flights - Fixed - Old AR 2019";
    internal const string Fixed = "Mercy Flights - Fixed";
    internal const string PhiMarker = "PHI-ORIGIN-MARKER";

    internal static async Task<NpgsqlWarehouse> CreateAsync(PostgresFixture postgres, IEnumerable<SeedTrip> trips, IEnumerable<SeedCash> cash, CancellationToken ct)
    {
        await using var admin = new NpgsqlConnection(TestDatabaseGuard.Assert(postgres.ConnectionString));
        await admin.OpenAsync(ct);
        await using (var ddl = new NpgsqlCommand(
            """
            CREATE SCHEMA IF NOT EXISTS ods;
            CREATE SCHEMA IF NOT EXISTS reporting;
            DROP TABLE IF EXISTS ods.accounting_period, ods.credit, ods.encounter_transaction, ods.encounter_transaction_credit_attribute, reporting.trip_detail_snapshot;
            CREATE TABLE ods.accounting_period (id int PRIMARY KEY, provider_id int NOT NULL, name varchar NOT NULL, is_open boolean NOT NULL DEFAULT false);
            CREATE TABLE ods.credit (id int PRIMARY KEY, credit_type_id int NOT NULL);
            CREATE TABLE ods.encounter_transaction (id bigint PRIMARY KEY, encounter_id bigint NOT NULL, type text NOT NULL, user_source varchar, accounting_period_id int NOT NULL, balance_amount int NOT NULL);
            CREATE TABLE ods.encounter_transaction_credit_attribute (id bigint PRIMARY KEY, encounter_transaction_id bigint NOT NULL, credit_id int NOT NULL);
            CREATE TABLE reporting.trip_detail_snapshot (
                snapshot_date date NOT NULL, encounter_id bigint NOT NULL, provider_id int NOT NULL, level_of_service text, response_type text,
                prim_pay text, prim_pay_category text, dos date, loaded_miles numeric, reporting_division text, vehicle text,
                origin_facility text, origin_mod text, origin_address text, origin_city text, origin_state text, origin_zip text);
            INSERT INTO ods.accounting_period VALUES (1, 2026, '202610', true), (2, 2026, '202609', false), (3, 2026, '-202610', true), (4, 2026, '202611', true), (9, 777, '202610', false);
            INSERT INTO ods.credit VALUES (2, 2);
            """,
            admin))
        {
            await ddl.ExecuteNonQueryAsync(ct);
        }

        foreach (SeedTrip t in trips)
        {
            await using var insert = new NpgsqlCommand(
                """
                INSERT INTO reporting.trip_detail_snapshot VALUES (@load::date, @enc, @prov, @los, @rt, @pay, @cat, @dos::date, @miles, @div, @veh, @fac, @mod, @pm, @pm, @pm, @pm)
                """,
                admin);
            insert.Parameters.AddWithValue("load", t.Load);
            insert.Parameters.AddWithValue("enc", t.Encounter);
            insert.Parameters.AddWithValue("prov", t.Provider);
            insert.Parameters.AddWithValue("los", (object?)t.Los ?? DBNull.Value);
            insert.Parameters.AddWithValue("rt", (object?)t.ResponseType ?? DBNull.Value);
            insert.Parameters.AddWithValue("pay", (object?)t.Pay ?? DBNull.Value);
            insert.Parameters.AddWithValue("cat", (object?)t.Category ?? DBNull.Value);
            insert.Parameters.AddWithValue("dos", t.Dos);
            insert.Parameters.AddWithValue("miles", (object?)t.Miles ?? DBNull.Value);
            insert.Parameters.AddWithValue("div", t.Division);
            insert.Parameters.AddWithValue("veh", (object?)t.Vehicle ?? DBNull.Value);
            insert.Parameters.AddWithValue("fac", (object?)t.Facility ?? DBNull.Value);
            insert.Parameters.AddWithValue("mod", (object?)t.Mod ?? DBNull.Value);
            insert.Parameters.AddWithValue("pm", PhiMarker);
            await insert.ExecuteNonQueryAsync(ct);
        }

        foreach (SeedCash c in cash)
        {
            await using var insert = new NpgsqlCommand(
                """
                INSERT INTO ods.encounter_transaction VALUES (@id, @enc, 'Credit', @src, @per, @cents);
                INSERT INTO ods.encounter_transaction_credit_attribute VALUES (@id, @id, 2)
                """,
                admin);
            insert.Parameters.AddWithValue("id", c.Id);
            insert.Parameters.AddWithValue("enc", c.Encounter);
            insert.Parameters.AddWithValue("src", c.Source);
            insert.Parameters.AddWithValue("per", c.Period);
            insert.Parameters.AddWithValue("cents", c.Cents);
            await insert.ExecuteNonQueryAsync(ct);
        }

        var options = new PrismWarehouseOptions { ConnectionString = postgres.ConnectionString };
        return new NpgsqlWarehouse(WarehouseDataSourceFactory.Create(options), Options.Create(options), NullLogger<NpgsqlWarehouse>.Instance);
    }

    internal static readonly ReportingDivision RotorDivision = new("rotor", Rotor);
    internal static readonly ReportingDivision FixedDivision = new("fixed", Fixed);
}
