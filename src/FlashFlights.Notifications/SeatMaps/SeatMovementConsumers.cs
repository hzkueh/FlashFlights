using FlashFlights.Contracts;
using MassTransit;

namespace FlashFlights.Notifications.SeatMaps;

/// <summary>
/// The bus-facing edge of the live seat map. Each consumer takes one of
/// Ordering's committed movement events and pushes the resulting per-Seat status
/// to that Flight's live viewers through the <see cref="ISeatMapBroadcaster"/>.
///
/// The event type <em>is</em> the new status — a <see cref="SeatsHeld"/> means
/// those Seats are now Held, a <see cref="SeatsReleased"/> means Available, a
/// <see cref="SeatsConfirmed"/> means Confirmed — so no read of Ordering is
/// needed to know what to paint. Kept as three tiny classes, mirroring Catalog's
/// projector consumers, so each event gets its own prefixed queue and a failure
/// names the event it was relaying.
///
/// These pushes are advisory decoration, exactly as Catalog's counts are: a
/// viewer acting on a stale cell is still refused by Ordering's locked grant
/// (ADR-0001), so a push lost or delayed can only make a map briefly wrong,
/// never let two buyers win the same Seat.
/// </summary>
public sealed class SeatsHeldLiveConsumer(ISeatMapBroadcaster broadcaster) : IConsumer<SeatsHeld>
{
    public Task Consume(ConsumeContext<SeatsHeld> context) =>
        broadcaster.SeatsChangedAsync(
            context.Message.FlightId,
            SeatChanges.Of(context.Message.SeatIds, LiveSeatStatus.Held),
            context.Message.OccurredAt,
            context.CancellationToken);
}

/// <inheritdoc cref="SeatsHeldLiveConsumer"/>
public sealed class SeatsReleasedLiveConsumer(ISeatMapBroadcaster broadcaster) : IConsumer<SeatsReleased>
{
    public Task Consume(ConsumeContext<SeatsReleased> context) =>
        broadcaster.SeatsChangedAsync(
            context.Message.FlightId,
            SeatChanges.Of(context.Message.SeatIds, LiveSeatStatus.Available),
            context.Message.OccurredAt,
            context.CancellationToken);
}

/// <inheritdoc cref="SeatsHeldLiveConsumer"/>
public sealed class SeatsConfirmedLiveConsumer(ISeatMapBroadcaster broadcaster) : IConsumer<SeatsConfirmed>
{
    public Task Consume(ConsumeContext<SeatsConfirmed> context) =>
        broadcaster.SeatsChangedAsync(
            context.Message.FlightId,
            SeatChanges.Of(context.Message.SeatIds, LiveSeatStatus.Confirmed),
            context.Message.OccurredAt,
            context.CancellationToken);
}

internal static class SeatChanges
{
    /// <summary>Every Seat in one committed movement lands on the same status.</summary>
    public static IReadOnlyList<SeatChange> Of(IReadOnlyList<Guid> seatIds, LiveSeatStatus status) =>
        [.. seatIds.Select(id => new SeatChange(id, status))];
}
