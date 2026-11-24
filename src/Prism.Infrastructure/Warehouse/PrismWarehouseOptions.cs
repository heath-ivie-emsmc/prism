using Npgsql;

namespace Prism.Infrastructure.Warehouse;

/// <summary>
/// The data warehouse. Read-only: Prism has no migrations and no write access to it. The connection string is a secret and comes
/// from user secrets or AWS Secrets Manager. It may be empty in Development, which leaves the warehouse unavailable.
/// </summary>
public sealed class PrismWarehouseOptions
{
    public const string ConnectionStringName = "Warehouse";
    public const string ConfigurationSection = "Warehouse";

    public string ConnectionString { get; set; } = "";

    /// <summary>Most connections Prism may hold open to the warehouse, so a traffic spike cannot exhaust it.</summary>
    public int MaxPoolSize { get; set; } = 20;

    /// <summary>A query that runs longer is cancelled, by the client and by the server.</summary>
    public int CommandTimeoutSeconds { get; set; } = 30;

    /// <summary>A query that returns more rows than this fails. Catalog queries aggregate, so the real answers are small.</summary>
    public int MaxRows { get; set; } = 10_000;

    /// <summary>
    /// Development only: ask the warehouse about this Provider whatever the signed-in user's Provider is, for testing before the
    /// test Provider has warehouse data. The user is still authorized for their own Provider first. A deployed host refuses to start with this set.
    /// </summary>
    public int? DevelopmentProviderId { get; set; }

    /// <summary>
    /// Temporary, local use only: skip the startup check of the login's privileges (for example while a SELECT-only login is being issued).
    /// It never lets Prism write. Logged as an error on every start. Every query still runs in a read-only transaction. A deployed host refuses to start with this set.
    /// </summary>
    public bool SkipLoginCheck { get; set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ConnectionString);
}

public static class PrismWarehouseOptionsRules
{
    private static readonly SslMode[] SafeModes = [SslMode.Require, SslMode.VerifyCA, SslMode.VerifyFull];

    private static void AddOverrideProblem(PrismWarehouseOptions options, bool deployed, List<string> problems)
    {
        if (deployed && options.SkipLoginCheck)
        {
            problems.Add("Warehouse:SkipLoginCheck must not be set in a deployed environment.");
        }
    }

    /// <summary>Returns the problems with these options, or none. A deployed host must have a warehouse and must encrypt the connection.</summary>
    public static IReadOnlyList<string> Problems(PrismWarehouseOptions options, bool deployed)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<string> problems = [];
        AddOverrideProblem(options, deployed, problems);

        if (options.MaxPoolSize is < 1 or > 200)
        {
            problems.Add("Warehouse:MaxPoolSize must be between 1 and 200.");
        }

        if (options.CommandTimeoutSeconds is < 1 or > 300)
        {
            problems.Add("Warehouse:CommandTimeoutSeconds must be between 1 and 300.");
        }

        if (options.MaxRows is < 1 or > 1_000_000)
        {
            problems.Add("Warehouse:MaxRows must be between 1 and 1000000.");
        }

        if (deployed && options.DevelopmentProviderId is not null)
        {
            problems.Add("Warehouse:DevelopmentProviderId must not be set in a deployed environment.");
        }

        if (!options.IsConfigured)
        {
            if (deployed)
            {
                problems.Add("ConnectionStrings:Warehouse is required in a deployed environment.");
            }

            return problems;
        }

        NpgsqlConnectionStringBuilder builder;
        try
        {
            builder = new NpgsqlConnectionStringBuilder { ConnectionString = options.ConnectionString };
        }
        catch (ArgumentException)
        {
            // Never echo the string: it holds the password.
            problems.Add("ConnectionStrings:Warehouse is not a valid PostgreSQL connection string.");
            return problems;
        }

        if (deployed && !SafeModes.Contains(builder.SslMode))
        {
            problems.Add("ConnectionStrings:Warehouse must set SSL Mode to Require, VerifyCA or VerifyFull in a deployed environment (VerifyFull preferred).");
        }

        return problems;
    }
}
