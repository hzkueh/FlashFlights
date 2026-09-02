using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FlashFlights.ServiceDefaults.DataStores;

/// <summary>
/// Readiness probe backed by the service's own <typeparamref name="TContext"/>,
/// so there is exactly one configured path to the store rather than a
/// hand-rolled ADO connection alongside the one the app actually uses.
/// </summary>
public sealed class DbContextDataStoreProbe<TContext>(string name, IServiceScopeFactory scopes)
    : IDataStoreProbe
    where TContext : DbContext
{
    public string Name => name;

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<TContext>().Database;

        // Executing, not just opening: the pool hands back an idle connection
        // without touching the server, so a stopped database still looks fine.
        await database.ExecuteSqlRawAsync("SELECT 1", cancellationToken);
    }
}
