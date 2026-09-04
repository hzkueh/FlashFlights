namespace FlashFlights.Ordering.Holds;

/// <summary>
/// What one run of the expiry sweep did: the number of Holds it expired and the
/// number of Seats it released back to the pool. Returned so the background
/// service can log a run that actually did work, and so the manual trigger has
/// something to report.
/// </summary>
public sealed record ExpireHoldsResult(int HoldsExpired, int SeatsReleased);
