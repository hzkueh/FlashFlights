namespace FlashFlights.Ordering.Domain;

/// <summary>
/// The three things that can happen to a Seat (CONTEXT.md). Values are
/// explicit because they are persisted: renumbering them would silently
/// reinterpret every historic movement in the ledger.
/// </summary>
public enum SeatMovementType
{
    /// <summary>A buyer started checkout on the Seat.</summary>
    Held = 1,

    /// <summary>A Held expired without confirming, returning the Seat to the pool.</summary>
    Released = 2,

    /// <summary>A Held became a Booking.</summary>
    Confirmed = 3,
}
