using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FlashFlights.ServiceDefaults.DataStores;

/// <summary>
/// Applies <typeparamref name="TContext"/>'s EF Core migrations. Every service
/// migrates itself on startup so a fresh <c>docker compose up</c> needs no
/// manual step — see <see cref="DataStoreStartupService"/> for the retry.
/// </summary>
public sealed class DbContextDataStoreMigrator<TContext>(string name, IServiceScopeFactory scopes)
    : IDataStoreMigrator
    where TContext : DbContext
{
    public string Name => name;

    public async Task MigrateAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<TContext>().Database;

        await database.MigrateAsync(cancellationToken);
    }
}
