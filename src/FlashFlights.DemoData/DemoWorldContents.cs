namespace FlashFlights.DemoData;

/// <summary>
/// A demo buyer. The password is shared by all of them and printed in this
/// repository on purpose — a reviewer has to be able to sign in as someone with
/// a booking history, and there is no registration step that would give them one
/// (CONTEXT.md: Users are the only actor, and flights are seeded rather than
/// authored).
/// </summary>
public sealed record DemoUser(string Key, string Email)
{
    /// <summary>Derived from <see cref="Key"/>, so every service agrees on it — see <see cref="DemoIds"/>.</summary>
    public Guid Id { get; } = DemoIds.User(Key);
}

/// <summary>One Seat in a demo Flight's cabin, with the label a buyer reads.</summary>
public sealed record DemoSeat(Guid Id, int Row, string Column)
{
    public string SeatNumber => $"{Row}{Column}";
}

/// <summary>
/// One buyer's claim on some Seats: a Hold, and — when it resolved — the Booking
/// it became.
///
/// <para>
/// One record rather than two, because that is the shape of the thing being
/// described. A Booking is "a Hold that resolved into Confirmed" (CONTEXT.md);
/// it has no seats, price, or buyer of its own that the Hold does not already
/// carry, so a second record here could only repeat them and then disagree.
/// <see cref="ConfirmedAt"/> is the whole difference, exactly as a resolving
/// movement is the whole difference in the ledger.
/// </para>
/// </summary>
/// <param name="BuyerId">
/// Who holds it. A plain id rather than a <see cref="DemoUser"/>, because most
/// of the Seats sold on a busy Flight belong to passengers with no account —
/// and Ordering could not tell the difference either, since a Hold's UserId is
/// a cross-service reference and never an FK.
/// </param>
/// <param name="ConfirmedAt">When the Hold became a Booking, or null while it is still Held.</param>
public sealed record DemoHold(
    Guid Id,
    Guid BuyerId,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? ConfirmedAt,
    decimal PricePerSeat,
    IReadOnlyList<DemoSeat> Seats)
{
    public bool IsConfirmed => ConfirmedAt is not null;

    /// <summary>What the simulated payment took — PricePerSeat times the Seats confirmed.</summary>
    public decimal PricePaid => PricePerSeat * Seats.Count;

    /// <summary>
    /// When this claim last moved the ledger: the confirmation if it resolved,
    /// otherwise the Held movement that created it.
    /// </summary>
    public DateTimeOffset LastMovementAt => ConfirmedAt ?? CreatedAt;
}

/// <summary>
/// One demo Flight: its route and window, the cabin it sells, and every claim
/// made on that cabin. Catalog, Ordering, and Notifications each read the parts
/// of this they own and ignore the rest — which is what keeps the counts on the
/// list page agreeing with the seat map they trail, without either service
/// reading the other's store (ADR-0001).
/// </summary>
public sealed record DemoFlight(
    Guid Id,
    string FlightNumber,
    string Origin,
    string Destination,
    DateTimeOffset DepartureAt,
    decimal FlashPrice,
    decimal? ReferenceFare,
    DateTimeOffset SaleStartsAt,
    DateTimeOffset SaleEndsAt,
    IReadOnlyList<DemoSeat> Seats,
    IReadOnlyList<DemoHold> Holds)
{
    public int TotalSeats => Seats.Count;

    public int HeldSeats => Holds.Where(hold => !hold.IsConfirmed).Sum(hold => hold.Seats.Count);

    public int ConfirmedSeats => Holds.Where(hold => hold.IsConfirmed).Sum(hold => hold.Seats.Count);

    public int AvailableSeats => TotalSeats - HeldSeats - ConfirmedSeats;

    /// <summary>
    /// The newest seeded movement on this Flight, or the epoch when nothing has
    /// claimed a Seat yet. It is Catalog's high-water mark for the SeatCounts
    /// projection: seeded movements are all in the past, so the first real
    /// movement a buyer causes is always newer and is folded in rather than
    /// dropped as a straggler.
    /// </summary>
    public DateTimeOffset LastMovementAt => Holds.Count == 0
        ? DateTimeOffset.UnixEpoch
        : Holds.Max(hold => hold.LastMovementAt);
}

/// <summary>A demo buyer's subscription to a Flight's sale opening.</summary>
public sealed record DemoWatch(Guid Id, DemoUser Watcher, DemoFlight Flight, DateTimeOffset CreatedAt);

/// <summary>
/// A Notification a demo buyer has already been sent — a Watch of theirs that fired
/// back when the Flight's sale opened. Carries no body text: what the sentence
/// says is Notifications' to decide, and a second copy of it here could only
/// drift from the one real Notifications are written with.
/// </summary>
public sealed record DemoNotification(
    Guid Id,
    DemoUser Recipient,
    DemoFlight Flight,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ReadAt);
