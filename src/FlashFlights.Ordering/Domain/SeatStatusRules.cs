namespace FlashFlights.Ordering.Domain;

/// <summary>
/// The latest movement on one Seat, reduced to just what deciding its status
/// needs: the movement's type and — when it is a Held — the expiry of the Hold
/// that posted it. A Held that is past its Hold's TTL computes back to Available
/// without the sweep having run (ADR-0001), which is why the expiry travels with
/// the movement here.
/// </summary>
public readonly record struct LatestMovement(SeatMovementType Type, DateTimeOffset HoldExpiresAt);

/// <summary>
/// The one place a Seat's status is derived and the one place a Hold's TTL
/// boundary is decided. Both the read side (granting a new Hold) and the sweep
/// call <see cref="HasExpired"/>, so a Hold is never simultaneously "expired" to
/// a reader and "live" to a confirm — the guarantee ticket 05 asks for is a
/// single function rather than two rules kept in step by hand.
/// </summary>
public static class SeatStatusRules
{
    /// <summary>
    /// A Seat's status from its latest movement as of <paramref name="now"/>.
    /// No movement, or a Released one, is Available; a Confirmed one is gone for
    /// good; a Held one is Held until its TTL, then Available again.
    /// </summary>
    public static SeatStatus StatusOf(LatestMovement? latest, DateTimeOffset now) => latest switch
    {
        null => SeatStatus.Available,
        { Type: SeatMovementType.Released } => SeatStatus.Available,
        { Type: SeatMovementType.Confirmed } => SeatStatus.Confirmed,
        { Type: SeatMovementType.Held } held =>
            HasExpired(held.HoldExpiresAt, now) ? SeatStatus.Available : SeatStatus.Held,
        _ => SeatStatus.Available,
    };

    /// <summary>
    /// Whether a Hold has reached its TTL. The boundary is inclusive — at the
    /// exact instant a Hold expires it is expired, not live — so the tie is
    /// resolved the same way for a reader deciding a Seat is takeable and for a
    /// confirm deciding the Hold is too late.
    /// </summary>
    public static bool HasExpired(DateTimeOffset expiresAt, DateTimeOffset now) => now >= expiresAt;
}
