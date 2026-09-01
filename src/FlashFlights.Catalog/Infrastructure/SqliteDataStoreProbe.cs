using FlashFlights.ServiceDefaults.DataStores;
using Microsoft.Data.Sqlite;

namespace FlashFlights.Catalog.Infrastructure;

/// <summary>
/// Opens Catalog's SQLite file, creating it if it does not exist yet (the
/// containing directory must already exist — compose mounts it). Catalog
/// carries no row-locking requirement (ADR-0001), so SQLite is sufficient here.
/// </summary>
public sealed class SqliteDataStoreProbe(IConfiguration configuration) : IDataStoreProbe
{
    private readonly string _connectionString =
        configuration.GetConnectionString("CatalogDb")
        ?? throw new InvalidOperationException("ConnectionStrings:CatalogDb is not configured.");

    public string Name => "catalog-sqlite";

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        // Executing, not just opening: a pooled connection can be handed back
        // without the store itself being touched at all.
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1";
        await command.ExecuteScalarAsync(cancellationToken);
    }
}
