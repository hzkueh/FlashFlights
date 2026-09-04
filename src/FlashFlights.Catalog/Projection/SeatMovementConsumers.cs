using FlashFlights.Contracts;
using MassTransit;

namespace FlashFlights.Catalog.Projection;

/// <summary>
/// The bus-facing edge of the projection. Each consumer does one thing: hand its
/// event's Flight, Seat count, and timestamp to the <see cref="SeatCountsProjector"/>.
/// Kept as three tiny classes rather than one switch so MassTransit gives each
/// event its own queue and retry behaviour, and so a failure names the event it
/// was folding in.
///
/// A Seat count, not the Seat ids: the counts move Seats between buckets by
/// tally (ADR-0001), so how many changed is all the projection needs. The ids
/// ride along on the event for a later consumer — the live seat map (ticket 08)
/// — that does care which Seats moved.
/// </summary>
public sealed class SeatsHeldConsumer(SeatCountsProjector projector) : IConsumer<SeatsHeld>
{
    public Task Consume(ConsumeContext<SeatsHeld> context) =>
        projector.ApplyHeldAsync(
            context.Message.FlightId,
            context.Message.SeatIds.Count,
            context.Message.OccurredAt,
            context.CancellationToken);
}

/// <inheritdoc cref="SeatsHeldConsumer"/>
public sealed class SeatsReleasedConsumer(SeatCountsProjector projector) : IConsumer<SeatsReleased>
{
    public Task Consume(ConsumeContext<SeatsReleased> context) =>
        projector.ApplyReleasedAsync(
            context.Message.FlightId,
            context.Message.SeatIds.Count,
            context.Message.OccurredAt,
            context.CancellationToken);
}

/// <inheritdoc cref="SeatsHeldConsumer"/>
public sealed class SeatsConfirmedConsumer(SeatCountsProjector projector) : IConsumer<SeatsConfirmed>
{
    public Task Consume(ConsumeContext<SeatsConfirmed> context) =>
        projector.ApplyConfirmedAsync(
            context.Message.FlightId,
            context.Message.SeatIds.Count,
            context.Message.OccurredAt,
            context.CancellationToken);
}
