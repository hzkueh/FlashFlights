using FlashFlights.Contracts;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace FlashFlights.Ordering.Holds;

/// <summary>
/// Publishes the seat-movement events onto the bus for Catalog's projector to
/// consume. The one adapter that knows both <see cref="ISeatMovementNotifier"/>
/// and MassTransit exist; everything above it speaks only the narrow interface.
///
/// Best-effort by design: it is called after a Hold's movements have committed
/// (ADR-0001), so a broker that is briefly unreachable must not turn a durable,
/// successful Hold into a failed request. A failed publish is logged and
/// swallowed — the ledger is already correct, and the browse counts it feeds are
/// advisory, so they simply stay behind until the next movement on that Flight
/// carries them forward.
/// </summary>
public sealed class MassTransitSeatMovementNotifier(
    IPublishEndpoint publish,
    ILogger<MassTransitSeatMovementNotifier> logger) : ISeatMovementNotifier
{
    public Task SeatsHeldAsync(Guid flightId, IReadOnlyList<Guid> seatIds, DateTimeOffset occurredAt, CancellationToken cancellationToken = default) =>
        PublishSafely(new SeatsHeld(flightId, seatIds, occurredAt), flightId, cancellationToken);

    public Task SeatsReleasedAsync(Guid flightId, IReadOnlyList<Guid> seatIds, DateTimeOffset occurredAt, CancellationToken cancellationToken = default) =>
        PublishSafely(new SeatsReleased(flightId, seatIds, occurredAt), flightId, cancellationToken);

    public Task SeatsConfirmedAsync(Guid flightId, IReadOnlyList<Guid> seatIds, DateTimeOffset occurredAt, CancellationToken cancellationToken = default) =>
        PublishSafely(new SeatsConfirmed(flightId, seatIds, occurredAt), flightId, cancellationToken);

    private async Task PublishSafely<TEvent>(TEvent message, Guid flightId, CancellationToken cancellationToken)
        where TEvent : class
    {
        try
        {
            await publish.Publish(message, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(
                exception,
                "Failed to publish {Event} for flight {FlightId}; the ledger is committed and browse counts will catch up on the next movement.",
                typeof(TEvent).Name,
                flightId);
        }
    }
}
