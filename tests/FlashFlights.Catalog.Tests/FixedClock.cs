namespace FlashFlights.Catalog.Tests;

/// <summary>
/// A clock stopped at one instant. Catalog reads the clock on three paths — the
/// list page's SaleState, the sale-start scan, and now the demo seed — and all
/// three are about what is true at a given moment rather than about time
/// passing, so a fixed reading is all any of them needs.
/// </summary>
internal sealed class FixedClock(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
