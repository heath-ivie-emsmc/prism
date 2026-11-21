using Microsoft.Extensions.DependencyInjection;

namespace Prism.Infrastructure.Persistence;

/// <summary>Answers whether Prism's own database accepts connections. Never throws, and never returns the reason.</summary>
public interface IDatabaseProbe
{
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken);
}

public sealed class PrismDatabaseProbe(IServiceScopeFactory scopes) : IDatabaseProbe
{
    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using AsyncServiceScope scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<PrismDbContext>();
            return await db.Database.CanConnectAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return false;
        }
    }
}
