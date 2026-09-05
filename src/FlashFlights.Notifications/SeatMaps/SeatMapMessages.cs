namespace FlashFlights.Notifications.SeatMaps;

/// <summary>
/// A Seat's status as a live viewer names it — the same three words Ordering's
/// seat map uses (CONTEXT.md), re-declared here rather than referenced because
/// Notifications takes no dependency on Ordering: it learns of a movement only
/// through the bus events, and each event <em>is</em> the resulting status.
/// Serialized to clients by name, so these match the SPA's <c>SeatStatus</c>
/// union verbatim.
/// </summary>
public enum LiveSeatStatus
{
    /// <summary>A Released movement, or a Held one whose Hold lapsed — the Seat is takeable again.</summary>
    Available,

    /// <summary>A Held movement whose Hold has not resolved.</summary>
    Held,

    /// <summary>A Confirmed movement — the Seat is a Booking and gone for good.</summary>
    Confirmed,
}

/// <summary>
/// One Seat's new status, as pushed to a flight's live viewers. The status is
/// all a viewer needs to repaint a cell — it already holds the Seat's label and
/// grid position from the initial map read, and those never change.
/// </summary>
public sealed record SeatChange(Guid SeatId, LiveSeatStatus Status);

/// <summary>
/// The message pushed to every viewer of one Flight when a batch of its Seats
/// changes. Mirrors one committed movement from Ordering (a whole Hold, a whole
/// confirm, a whole release), so a batch shares one <see cref="OccurredAt"/>.
///
/// <para>
/// <see cref="OccurredAt"/> is Ordering's clock at the moment the movements were
/// written, carried through unchanged as a high-water mark rather than a
/// wall-clock. No viewer acts on it yet: over one ordered connection changes
/// already arrive in the order they were made, so today's client applies each
/// batch as it comes. It is threaded through now so the reconnect re-sync
/// (ticket 08, item 5) can drop a change that predates the state it re-reads,
/// without a later wire change.
/// </para>
/// </summary>
public sealed record SeatMapChanged(Guid FlightId, IReadOnlyList<SeatChange> Seats, DateTimeOffset OccurredAt);

/// <summary>
/// The narrow seam the movement consumers push through: hand it a Flight's Seat
/// changes and it reaches exactly that Flight's connected viewers. The one
/// interface that knows both the seat-map hub and SignalR exist, so the
/// consumers stay testable without a live connection — the same shape
/// <c>MassTransitSeatMovementNotifier</c> gives Ordering over the bus.
/// </summary>
public interface ISeatMapBroadcaster
{
    /// <summary>
    /// Pushes <paramref name="seats"/>' new statuses to the viewers subscribed to
    /// <paramref name="flightId"/>, and to no one else. Best-effort: a push that
    /// fails leaves the authoritative ledger untouched, and a viewer re-syncs to
    /// true state on its next read.
    /// </summary>
    Task SeatsChangedAsync(
        Guid flightId,
        IReadOnlyList<SeatChange> seats,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken = default);
}
