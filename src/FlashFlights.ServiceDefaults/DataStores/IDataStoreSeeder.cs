namespace FlashFlights.ServiceDefaults.DataStores;

/// <summary>
/// Puts a service's starting rows in place once its schema exists. Separate
/// from <see cref="IDataStoreMigrator"/> for the same reason the migrator is
/// separate from <see cref="IDataStoreProbe"/> — they answer different
/// questions. The migrator decides what shape the store has; a seeder decides
/// what is in it, and only a store that has never been seeded is its business.
///
/// <para>
/// Run by <see cref="DataStoreStartupService"/> after the migration and before
/// the service reports ready, so nothing is ever served a store that is
/// migrated but still empty. That places it inside the same retry loop, which
/// is why an implementation has to be both idempotent — a second run over a
/// store it already filled must change nothing — and atomic, so a run that
/// fails halfway leaves nothing behind for the next attempt to mistake for a
/// finished job.
/// </para>
/// </summary>
public interface IDataStoreSeeder
{
    /// <summary>Name used in logs, e.g. "catalog-demo".</summary>
    string Name { get; }

    /// <summary>
    /// Seeds the store if it is empty. Returns whether this run wrote anything,
    /// so the log can tell a first startup from every one after it. Throws if
    /// the seed could not be completed.
    /// </summary>
    Task<bool> SeedAsync(CancellationToken cancellationToken);
}
