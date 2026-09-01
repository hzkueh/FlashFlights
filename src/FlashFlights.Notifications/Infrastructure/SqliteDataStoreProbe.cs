using FlashFlights.ServiceDefaults.DataStores;
using Microsoft.Data.Sqlite;

namespace FlashFlights.Notifications.Infrastructure;

/// <summary>
/// Opens Notifications' SQLite file, creating it if it does not exist yet (the
/// containing directory must already exist — compose mounts it).
/// Notifications carries no row-locking requirement (ADR-0001).
/// </summary>
public sealed class SqliteDataStoreProbe(IConfiguration configuration) : IDataStoreProbe
{
    private readonly string _connectionString =
        configuration.GetConnectionString("NotificationsDb")
        ?? throw new InvalidOperationException("ConnectionStrings:NotificationsDb is not configured.");

    public string Name => "notifications-sqlite";

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
