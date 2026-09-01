namespace FlashFlights.Contracts;

/// <summary>
/// A throwaway wiring probe: proves a message published by one service is
/// really consumed by another over the bus, rather than that the containers
/// merely started. Delete once the real event contracts
/// (SeatsHeld / SeatsReleased / SeatsConfirmed / FlightSaleStarted) exist.
/// </summary>
/// <param name="PingId">Correlates the publish call with what the consumer recorded.</param>
/// <param name="Source">The service that published it.</param>
/// <param name="SentAt">When it was published.</param>
public sealed record PingSent(Guid PingId, string Source, DateTimeOffset SentAt);
