using Microsoft.Extensions.Options;

namespace Prism.Infrastructure.Persistence;

/// <summary>Prism's own database. The connection string is a secret and comes from user secrets or AWS Secrets Manager.</summary>
public sealed class PrismDatabaseOptions
{
    public const string ConnectionStringName = "Prism";

    public string ConnectionString { get; set; } = "";
}

public sealed class PrismDatabaseOptionsValidator : IValidateOptions<PrismDatabaseOptions>
{
    public ValidateOptionsResult Validate(string? name, PrismDatabaseOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return string.IsNullOrWhiteSpace(options.ConnectionString)
            ? ValidateOptionsResult.Fail("ConnectionStrings:Prism is required. Set it in user secrets locally, or in Secrets Manager when deployed.")
            : ValidateOptionsResult.Success;
    }
}
