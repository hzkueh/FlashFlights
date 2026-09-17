namespace FlashFlights.DemoData;

/// <summary>
/// One service's projection of the demo world into its own store — Catalog's
/// Flights, Ordering's ledger, Notifications' inbox, the gateway's buyers.
///
/// <para>
/// An implementation writes only what its service owns and reads no other
/// store; the ids line up because they are derived rather than exchanged
/// (<see cref="DemoIds"/>). It is handed the world rather than building it, so
/// the clock is read once per service and every row it writes is relative to
/// the same instant.
/// </para>
///
/// <para>
/// Two rules, both inherited from running inside the startup retry loop: seed
/// only a store that is empty of what this seeder owns, and write everything in
/// one transaction. Between them they are what "seeds on first startup and is
/// idempotent across restarts" actually means.
/// </para>
/// </summary>
public interface IDemoSeeder
{
    /// <summary>
    /// Seeds this service's store from <paramref name="world"/> if it has not
    /// been seeded, and returns whether it wrote anything.
    /// </summary>
    Task<bool> SeedAsync(DemoWorld world, CancellationToken cancellationToken = default);
}
