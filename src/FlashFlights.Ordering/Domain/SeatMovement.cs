namespace FlashFlights.Ordering.Domain;

/// <summary>
/// An append-only record of a single change to one Seat, and the source of
/// truth for that Seat's status (ADR-0001). Never edited and never deleted, and
/// enforced twice over: <c>OrderingDbContext</c> refuses to save an update or a
/// delete of one, and a database trigger rejects the paths that bypass change
/// tracking. The ledger stays an audit trail rather than a mutable table with
/// history-shaped columns.
/// </summary>
public sealed class SeatMovement
{
    public Guid Id { get; set; }

    public Guid SeatId { get; set; }

    /// <summary>
    /// The Hold that caused this movement. Never null — every movement traces
    /// to one: Held when it was granted, Released when it expired, Confirmed
    /// when it became a Booking.
    /// </summary>
    public Guid HoldId { get; set; }

    public SeatMovementType Type { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    public Seat? Seat { get; set; }

    public Hold? Hold { get; set; }
}
