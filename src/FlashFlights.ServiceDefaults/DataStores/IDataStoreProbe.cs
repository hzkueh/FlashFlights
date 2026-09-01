namespace FlashFlights.ServiceDefaults.DataStores;

/// <summary>
/// A single attempt to reach the service's own datastore. Implemented per
/// service because each service owns a different store — Ordering needs
/// PostgreSQL for real row-level locking (ADR-0001), the others use SQLite.
/// </summary>
public interface IDataStoreProbe
{
    /// <summary>Name used in logs and health output, e.g. "catalog-sqlite".</summary>
    string Name { get; }

    /// <summary>Throws if the datastore cannot currently be reached.</summary>
    Task ConnectAsync(CancellationToken cancellationToken);
}
