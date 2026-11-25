using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Prism.Api.Health;
using Prism.Application.Auth;
using Prism.Infrastructure.Auth;
using Npgsql;
using Prism.Application.Warehouse;
using Prism.Infrastructure.Persistence;
using Prism.Infrastructure.Warehouse;

namespace Prism.Api;

/// <summary>Composition root for Prism's own database.</summary>
public static class PersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddPrismPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IValidateOptions<PrismDatabaseOptions>, PrismDatabaseOptionsValidator>();
        services.AddOptions<PrismDatabaseOptions>()
            .Configure(options => options.ConnectionString = configuration.GetConnectionString(PrismDatabaseOptions.ConnectionStringName) ?? "")
            .ValidateOnStart();

        // The connection string is read each time a context is created, so a rotated secret reaches the next request.
        // The startup validation above still guarantees one exists. Contexts already in use finish on their own connection.
        services.AddDbContext<PrismDbContext>((provider, options) =>
            options.UsePrismNpgsql(provider.GetRequiredService<IConfiguration>().GetConnectionString(PrismDatabaseOptions.ConnectionStringName) ?? ""));
        services.AddScoped<ISessionRevocationStore, SessionRevocationStore>();
        services.AddScoped<Prism.Application.Audit.IAuditLog, AuditLog>();
        services.AddSingleton<IDatabaseProbe, PrismDatabaseProbe>();
        services.AddPrismRedis();
        services.AddPrismWarehouse();
        services.AddSingleton<ReadinessCheck>();

        return services;
    }

    /// <summary>
    /// The data warehouse: a separate, pooled, read-only connection used only through <see cref="IWarehouse"/>. Required in deployed environments.
    /// Without a connection string (Development) every query throws <see cref="WarehouseUnavailableException"/> and nothing else is affected.
    /// </summary>
    private static void AddPrismWarehouse(this IServiceCollection services)
    {
        services.AddSingleton<IValidateOptions<PrismWarehouseOptions>, WarehouseOptionsValidator>();
        services.AddOptions<PrismWarehouseOptions>()
            .BindConfiguration(PrismWarehouseOptions.ConfigurationSection)
            .Configure<IConfiguration>((options, configuration) =>
                options.ConnectionString = configuration.GetConnectionString(PrismWarehouseOptions.ConnectionStringName) ?? "")
            .ValidateOnStart();

        services.AddHostedService<WarehouseStartupNotice>();

        const string key = "warehouse";
        services.AddKeyedSingleton<NpgsqlDataSource>(key, (provider, _) =>
            WarehouseDataSourceFactory.Create(provider.GetRequiredService<IOptions<PrismWarehouseOptions>>().Value));

        // Write protection: in every environment, a configured warehouse login that can write, create or own anything stops the host from starting.
        // Registered after ValidateOnStart, so a misconfigured connection string is reported as such before the login is inspected.
        services.AddSingleton<IWarehouseWriteProtectionCheck>(provider =>
            provider.GetRequiredService<IOptions<PrismWarehouseOptions>>().Value.IsConfigured
                ? new WarehouseWriteProtectionCheck(
                    provider.GetRequiredKeyedService<NpgsqlDataSource>(key),
                    provider.GetRequiredService<IOptions<PrismWarehouseOptions>>(),
                    provider.GetRequiredService<ILogger<WarehouseWriteProtectionCheck>>())
                : new NotConfiguredWarehouseWriteProtectionCheck());
        services.AddHostedService<WarehouseWriteProtectionStartup>();
        // Follows ConnectionStrings:Warehouse as a rotated secret changes it. A changed login is proven read-only before any query uses it.
        services.AddSingleton<IWarehouseConnections>(provider =>
        {
            var warehouseOptions = provider.GetRequiredService<IOptions<PrismWarehouseOptions>>();
            var configuration = provider.GetRequiredService<IConfiguration>();
            var environment = provider.GetRequiredService<IHostEnvironment>();
            var loggers = provider.GetRequiredService<ILoggerFactory>();
            return new RotatingWarehouseConnections(
                () => configuration.GetConnectionString(PrismWarehouseOptions.ConnectionStringName) ?? "",
                warehouseOptions.Value,
                provider.GetRequiredKeyedService<NpgsqlDataSource>(key),
                environment.IsDeployedEnvironment(),
                (dataSource, candidate, cancellationToken) =>
                    new WarehouseWriteProtectionCheck(dataSource, Options.Create(candidate), loggers.CreateLogger<WarehouseWriteProtectionCheck>()).VerifyAsync(cancellationToken),
                provider.GetRequiredService<TimeProvider>(),
                loggers.CreateLogger<RotatingWarehouseConnections>());
        });
        services.AddSingleton<IWarehouse>(provider =>
            provider.GetRequiredService<IOptions<PrismWarehouseOptions>>().Value.IsConfigured
                ? new NpgsqlWarehouse(
                    provider.GetRequiredService<IWarehouseConnections>(),
                    provider.GetRequiredService<IOptions<PrismWarehouseOptions>>(),
                    provider.GetRequiredService<ILogger<NpgsqlWarehouse>>())
                : new UnconfiguredWarehouse());
    }

    /// <summary>
    /// Login replay protection lives in Redis when <c>ConnectionStrings:Redis</c> is set, because replay memory kept in one process
    /// does nothing across instances or restarts. Deployed environments must set it. Development and tests may use the in-memory store.
    /// </summary>
    private static void AddPrismRedis(this IServiceCollection services)
    {
        services.AddSingleton<IValidateOptions<PrismRedisOptions>, DeployedRedisOptionsValidator>();
        services.AddOptions<PrismRedisOptions>()
            .Configure<IConfiguration>((options, configuration) =>
                options.ConnectionString = configuration.GetConnectionString(PrismRedisOptions.ConnectionStringName) ?? "")
            .ValidateOnStart();

        services.AddSingleton<ILoginTokenReplayStore>(provider =>
        {
            string connectionString = provider.GetRequiredService<IOptions<PrismRedisOptions>>().Value.ConnectionString;
            return connectionString.Length > 0
                ? new RedisLoginTokenReplayStore(() => provider.GetRequiredService<IConfiguration>().GetConnectionString(PrismRedisOptions.ConnectionStringName) ?? "")
                : new InMemoryLoginTokenReplayStore(provider.GetRequiredService<TimeProvider>());
        });
    }
}

/// <summary>A deployed host with no Redis would silently lose replay protection across instances, so it refuses to start.</summary>
public sealed class DeployedRedisOptionsValidator(IHostEnvironment environment) : IValidateOptions<PrismRedisOptions>
{
    public ValidateOptionsResult Validate(string? name, PrismRedisOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return environment.IsDeployedEnvironment() && string.IsNullOrWhiteSpace(options.ConnectionString)
            ? ValidateOptionsResult.Fail("ConnectionStrings:Redis is required in a deployed environment (login replay protection must be shared).")
            : ValidateOptionsResult.Success;
    }
}

/// <summary>A deployed host must have a warehouse and an encrypted connection to it, and every host needs sane limits. Problems never echo the connection string.</summary>
public sealed class WarehouseOptionsValidator(IHostEnvironment environment) : IValidateOptions<PrismWarehouseOptions>
{
    public ValidateOptionsResult Validate(string? name, PrismWarehouseOptions options)
    {
        IReadOnlyList<string> problems = PrismWarehouseOptionsRules.Problems(options, environment.IsDeployedEnvironment());
        return problems.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(problems);
    }
}

/// <summary>Says at start-up when the warehouse is standing in a different Provider, so nobody mistakes the numbers for the user's own.</summary>
public sealed partial class WarehouseStartupNotice(IOptions<PrismWarehouseOptions> options, ILogger<WarehouseStartupNotice> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (options.Value.DevelopmentProviderId is { } providerId)
        {
            LogStandIn(logger, providerId);
        }
        else if (!options.Value.IsConfigured)
        {
            LogNotConfigured(logger);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(Level = LogLevel.Warning, Message = "Warehouse queries use stand-in Provider {ProviderId} instead of the signed-in user's Provider (Warehouse:DevelopmentProviderId, Development only)")]
    private static partial void LogStandIn(ILogger logger, int providerId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "No warehouse connection is configured (ConnectionStrings:Warehouse). Metric routes will answer 503")]
    private static partial void LogNotConfigured(ILogger logger);
}
