namespace FlashFlights.Contracts;

/// <summary>
/// Catalog's announcement that one Flight's flash sale has opened — the single
/// trigger a Watch fires on (CONTEXT.md). Published by Catalog's sale-start
/// scheduler when a Flight crosses its SaleStartsAt, and consumed by
/// Notifications, which turns it into a Notification for every User watching
/// that Flight.
///
/// <para>
/// One sale window per Flight, so this is announced once per Flight for the
/// life of the system — Catalog records that it announced, and never announces
/// the same Flight again. That marker is written after the publish, so a crash
/// in between re-announces rather than loses the announcement: Notifications'
/// inbox is unique per (User, Flight), so a redelivered announcement cannot
/// duplicate anyone's inbox, while a lost one would silently cost every watcher
/// the alert they asked for.
/// </para>
///
/// <para>
/// The route travels on the event rather than being looked up, because
/// Notifications takes no dependency on Catalog: it composes what the User
/// reads from this message alone, the same way the seat-map relay reads the
/// resulting status straight off the movement events.
/// </para>
/// </summary>
/// <param name="FlightId">The Flight whose sale opened.</param>
/// <param name="FlightNumber">Carrier designator as the buyer knows it, e.g. "FF412".</param>
/// <param name="Origin">Departure airport code, e.g. "LHR".</param>
/// <param name="Destination">Arrival airport code, e.g. "BCN".</param>
/// <param name="SaleEndsAt">When the window closes — how long a watcher has to act.</param>
/// <param name="OccurredAt">
/// Catalog's clock when the crossing was detected. Informational: it is the
/// scheduler's tick, not the exact SaleStartsAt instant, so it trails the real
/// crossing by up to one scheduler interval.
/// </param>
public sealed record FlightSaleStarted(
    Guid FlightId,
    string FlightNumber,
    string Origin,
    string Destination,
    DateTimeOffset SaleEndsAt,
    DateTimeOffset OccurredAt);
