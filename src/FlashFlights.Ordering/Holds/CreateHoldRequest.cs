namespace FlashFlights.Ordering.Holds;

/// <summary>
/// A request to hold specific Seats for one buyer. Everything cross-service on
/// it is caller-supplied and taken on trust: Catalog owns the Flight, the
/// Identity store owns the User, and Catalog owns the FlashPrice the buyer was
/// shown. Ordering validates only what it can see in its own ledger — that each
/// Seat exists and belongs to <see cref="FlightId"/> — which is what stops a
/// caller holding Seats under the wrong Flight without Ordering ever having to
/// call another service.
/// </summary>
/// <param name="FlightId">Cross-service reference to the Catalog Flight, not an FK.</param>
/// <param name="SeatIds">The specific Seats to hold. Must be non-empty, distinct, and all Seats of <paramref name="FlightId"/>.</param>
/// <param name="UserId">Cross-service reference to the Identity User, not an FK.</param>
/// <param name="PricePerSeat">The FlashPrice per Seat the buyer was shown, frozen onto the Hold so a later price change cannot alter the agreed terms.</param>
public sealed record CreateHoldRequest(
    Guid FlightId,
    IReadOnlyList<Guid> SeatIds,
    Guid UserId,
    decimal PricePerSeat);
