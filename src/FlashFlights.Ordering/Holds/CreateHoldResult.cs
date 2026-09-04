namespace FlashFlights.Ordering.Holds;

/// <summary>
/// The three ways a <see cref="IHoldService.CreateHoldAsync"/> can end, kept as
/// distinct types so the HTTP layer maps each to its own status code and the
/// SPA can tell them apart. The distinction that matters:
/// <see cref="Malformed"/> is a request that was wrong to begin with (a retry of
/// the same thing cannot help — 400), while <see cref="Conflict"/> is a
/// well-formed request that lost a race for Seats someone else holds (a retry on
/// other Seats might — 409).
/// </summary>
public abstract record CreateHoldResult
{
    private CreateHoldResult()
    {
    }

    /// <summary>The Hold was granted: its Held movements are committed to the ledger.</summary>
    public sealed record Granted(HoldView Hold) : CreateHoldResult;

    /// <summary>
    /// The request never reached the ledger — empty Seats, a Seat named twice,
    /// or Seats that are not part of the given Flight. Grouped by field the way
    /// ticket 04 established, so an auth-style form can render each next to its
    /// input.
    /// </summary>
    public sealed record Malformed(IReadOnlyDictionary<string, string[]> Errors) : CreateHoldResult;

    /// <summary>
    /// The request was well-formed but one or more Seats were already Held or
    /// Confirmed by the time the row lock was taken. Names them so the buyer is
    /// told which Seats to give up on rather than a bare failure — and so a
    /// partial grant never happens: either every Seat is held or none is.
    /// </summary>
    public sealed record Conflict(IReadOnlyList<ConflictingSeat> Seats) : CreateHoldResult;
}

/// <summary>What the caller gets back when a Hold is granted.</summary>
public sealed record HoldView(
    Guid HoldId,
    Guid FlightId,
    Guid UserId,
    DateTimeOffset ExpiresAt,
    decimal PricePerSeat,
    IReadOnlyList<HeldSeatView> Seats);

/// <summary>One Seat carried on a granted Hold, with the label a buyer reads.</summary>
public sealed record HeldSeatView(Guid SeatId, string SeatNumber);

/// <summary>A Seat that blocked a Hold, and the status that blocked it.</summary>
public sealed record ConflictingSeat(Guid SeatId, string SeatNumber, string Status);
