namespace FlashFlights.Contracts;

/// <summary>
/// The events Ordering publishes each time it commits SeatMovements, and
/// Catalog's SeatCounts projector consumes to keep its per-Flight tally fresh
/// (spec, ADR-0001). One event per committed operation — a whole Hold's Seats,
/// a whole confirm, a whole release — never one per Seat, so a batch shares a
/// single <see cref="OccurredAt"/>.
///
/// <para>
/// These are advisory decoration for browsing. Ordering never reads what
/// Catalog does with them when granting a Hold, so an event lost, doubled, or
/// delivered late can only make the browse counts briefly wrong — never let two
/// buyers win the same Seat.
/// </para>
///
/// <para>
/// <see cref="OccurredAt"/> is Ordering's own clock at the moment the movements
/// were written, carried so the projector can drop an event it has already
/// folded in (a redelivery) or one that arrives behind a newer one, rather than
/// double-counting it. It is a high-water mark, not a wall-clock the consumer
/// trusts for anything else.
/// </para>
/// </summary>
public sealed record SeatsHeld(Guid FlightId, IReadOnlyList<Guid> SeatIds, DateTimeOffset OccurredAt);

/// <summary>
/// Seats a Hold gave back — the compensating Released movements the expiry
/// sweep posts when a Hold reaches its TTL unconfirmed. This is the event that
/// heals Catalog's counts after a silent expiry: the seat map self-heals the
/// instant the TTL passes, but the counts learn of it only here (ADR-0001), so
/// the sweep runs well under the TTL to bound how long the two can disagree.
/// </summary>
/// <inheritdoc cref="SeatsHeld"/>
public sealed record SeatsReleased(Guid FlightId, IReadOnlyList<Guid> SeatIds, DateTimeOffset OccurredAt);

/// <summary>
/// Seats a Hold confirmed into a Booking — the terminal movement for each. Once
/// folded into the counts these Seats leave the Available pool for good.
/// </summary>
/// <inheritdoc cref="SeatsHeld"/>
public sealed record SeatsConfirmed(Guid FlightId, IReadOnlyList<Guid> SeatIds, DateTimeOffset OccurredAt);
