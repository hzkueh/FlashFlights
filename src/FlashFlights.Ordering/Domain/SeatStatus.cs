namespace FlashFlights.Ordering.Domain;

/// <summary>
/// The computed condition of a Seat (CONTEXT.md) — derived from its latest
/// SeatMovement, never stored. This enum exists only to name the result of that
/// computation; it is deliberately not a mapped column, and the schema tests
/// prove no table carries it.
/// </summary>
public enum SeatStatus
{
    /// <summary>No live movement holds it: never held, released, or a Held past its TTL.</summary>
    Available,

    /// <summary>A Held movement whose Hold has not yet expired.</summary>
    Held,

    /// <summary>A Confirmed movement — the Seat became a Booking and is gone for good.</summary>
    Confirmed,
}
