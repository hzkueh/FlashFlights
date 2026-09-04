namespace FlashFlights.Ordering.Domain;

/// <summary>
/// One Seat's latest movement, reduced to what every reader of the ledger needs
/// between them: the movement's type, the Hold that posted it (the sweep checks
/// ownership), and that Hold's expiry (the status rules need it for a Held).
///
/// Which movement is "latest" is the load-bearing bit — newest OccurredAt, then
/// newest Id to break a same-instant tie (ADR-0001). That rule is decided once,
/// in <see cref="Persistence.SeatLedger"/>, so the grant path, the sweep, and the
/// seat map cannot drift on it.
/// </summary>
public readonly record struct SeatLedgerHead(SeatMovementType Type, Guid HoldId, DateTimeOffset HoldExpiresAt)
{
    /// <summary>The view <see cref="SeatStatusRules"/> reads — just the type and, for a Held, its expiry.</summary>
    public LatestMovement ToStatusInput() => new(Type, HoldExpiresAt);
}
