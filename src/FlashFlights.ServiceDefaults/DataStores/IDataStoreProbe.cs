namespace FlashFlights.ServiceDefaults.DataStores;

/// <summary>
/// A single attempt to reach the service's own datastore, used by
/// <see cref="DataStoreHealthCheck"/> to answer "is the store still there" on
/// every readiness hit. Kept as an interface so the health check's behaviour
/// can be tested against a store that fails on demand.
/// </summary>
public interface IDataStoreProbe
{
    /// <summary>Name used in logs and health output, e.g. "catalog-sqlite".</summary>
    string Name { get; }

    /// <summary>Throws if the datastore cannot currently be reached.</summary>
    Task ConnectAsync(CancellationToken cancellationToken);
}
