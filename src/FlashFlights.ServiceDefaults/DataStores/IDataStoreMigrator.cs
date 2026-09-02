namespace FlashFlights.ServiceDefaults.DataStores;

/// <summary>
/// Brings the service's own schema up to date. Separate from
/// <see cref="IDataStoreProbe"/> because the two answer different questions:
/// the migrator runs once at startup and must succeed before the service can
/// serve anything, while the probe answers "is the store still there" on every
/// readiness hit.
/// </summary>
public interface IDataStoreMigrator
{
    /// <summary>Name used in logs, e.g. "ordering-postgres".</summary>
    string Name { get; }

    /// <summary>Throws if the schema could not be brought up to date.</summary>
    Task MigrateAsync(CancellationToken cancellationToken);
}
