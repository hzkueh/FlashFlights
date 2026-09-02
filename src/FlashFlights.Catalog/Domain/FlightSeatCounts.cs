namespace FlashFlights.Catalog.Domain;

/// <summary>
/// Catalog's eventually-consistent projection of a Flight's seat availability,
/// maintained from the SeatsHeld / SeatsReleased / SeatsConfirmed events
/// Ordering publishes.
///
/// Browsing decoration only: Ordering never reads this when granting a Hold
/// (ADR-0001), so this being stale can only make a buyer's list view slightly
/// out of date — it can never let two buyers win the same Seat.
///
/// Counts, deliberately — not per-Seat status. A per-Seat status column here
/// would be precisely the stored, mutable SeatStatus that ADR-0001 rules out.
/// </summary>
public sealed class FlightSeatCounts
{
    /// <summary>Primary key as well as the reference to the Flight — one row per Flight.</summary>
    public Guid FlightId { get; set; }

    public int TotalSeats { get; set; }

    public int AvailableSeats { get; set; }

    public int HeldSeats { get; set; }

    public int ConfirmedSeats { get; set; }

    /// <summary>
    /// When the newest movement folded into these counts occurred — Ordering's
    /// clock, not Catalog's, so an event that arrives out of order or twice can
    /// be recognised and dropped rather than double-counted.
    /// </summary>
    public DateTimeOffset LastMovementAt { get; set; }

    public Flight? Flight { get; set; }
}
