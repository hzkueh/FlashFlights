using FlashFlights.ServiceDefaults.DataStores;
using Npgsql;

namespace FlashFlights.Ordering.Infrastructure;

/// <summary>
/// Round-trips a trivial query against Ordering's PostgreSQL. Ordering is
/// PostgreSQL rather than SQLite because its no-double-hold guarantee needs
/// real row-level locking (ADR-0001) — and because a containerised database is
/// exactly the dependency that is not ready when this container starts.
/// </summary>
public sealed class PostgresDataStoreProbe(IConfiguration configuration) : IDataStoreProbe
{
    private readonly string _connectionString =
        configuration.GetConnectionString("OrderingDb")
        ?? throw new InvalidOperationException("ConnectionStrings:OrderingDb is not configured.");

    public string Name => "ordering-postgres";

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        // Opening alone proves nothing: the pool hands back an idle connection
        // without touching the server, so a stopped database still looks fine.
        // Executing forces a real round-trip.
        await using var command = new NpgsqlCommand("SELECT 1", connection);
        await command.ExecuteScalarAsync(cancellationToken);
    }
}
