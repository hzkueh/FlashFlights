namespace FlashFlights.Ordering.Holds;

/// <summary>
/// The ways a <see cref="IHoldService.ConfirmHoldAsync"/> can end, kept as
/// distinct types so the HTTP layer maps each to its own status code and the SPA
/// can tell them apart. The distinction that matters at checkout:
/// <see cref="Expired"/> means the TTL passed before the buyer paid — the Seats
/// are likely free again, so starting over may still succeed — while
/// <see cref="AlreadyConfirmed"/> means the Hold already became a Booking and
/// there is nothing left to do. Both are lost causes for <em>this</em> confirm,
/// but only one invites a retry.
/// </summary>
public abstract record ConfirmHoldResult
{
    private ConfirmHoldResult()
    {
    }

    /// <summary>The Hold became a Booking: its Confirmed movements are committed to the ledger.</summary>
    public sealed record Confirmed(BookingView Booking) : ConfirmHoldResult;

    /// <summary>
    /// No live Hold with that id belongs to the caller — either it never existed
    /// or it is someone else's. The two are one answer on purpose: a buyer may
    /// only confirm their own Hold, and telling "not yours" from "not real" apart
    /// would leak which Hold ids exist.
    /// </summary>
    public sealed record NotFound : ConfirmHoldResult;

    /// <summary>
    /// The Hold reached its TTL before it was confirmed. It reads as expired to
    /// the same clock the read side uses, so it is too late to confirm — never
    /// live to a confirm while expired to a reader. The Seats have likely
    /// returned to the pool, so a fresh Hold may still win them.
    /// </summary>
    public sealed record Expired : ConfirmHoldResult;

    /// <summary>
    /// The Hold already resolved into a Booking. A resolved Hold cannot be
    /// re-confirmed; the second confirm of a race lands here rather than tripping
    /// the one-Booking-per-Hold constraint.
    /// </summary>
    public sealed record AlreadyConfirmed : ConfirmHoldResult;
}

/// <summary>What the caller gets back when a Hold is confirmed into a Booking.</summary>
public sealed record BookingView(
    Guid BookingId,
    Guid HoldId,
    Guid UserId,
    DateTimeOffset ConfirmedAt,
    decimal PricePaid,
    IReadOnlyList<BookedSeatView> Seats);

/// <summary>One Seat carried on a Booking, with the label a buyer reads.</summary>
public sealed record BookedSeatView(Guid SeatId, string SeatNumber);
