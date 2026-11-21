using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Prism.Api.Health;
using Prism.Application.Auth;
using Prism.Infrastructure.Auth;
using Prism.Infrastructure.Persistence;

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

        services.AddDbContext<PrismDbContext>((provider, options) =>
            options.UsePrismNpgsql(provider.GetRequiredService<IOptions<PrismDatabaseOptions>>().Value.ConnectionString));
        services.AddScoped<ISessionRevocationStore, SessionRevocationStore>();
        services.AddSingleton<IDatabaseProbe, PrismDatabaseProbe>();
        services.AddPrismRedis();
        services.AddSingleton<ReadinessCheck>();

        return services;
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
                ? new RedisLoginTokenReplayStore(connectionString)
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
