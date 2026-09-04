using FlashFlights.Ordering.Holds;

namespace FlashFlights.Ordering.Tests;

/// <summary>
/// Records every announcement HoldService makes, so a test can assert the right
/// event went out for the right Seats without standing up a bus. Stands in for
/// the MassTransit-backed notifier the real host wires.
/// </summary>
internal sealed class RecordingSeatMovementNotifier : ISeatMovementNotifier
{
    public List<SeatMovementAnnouncement> Held { get; } = [];

    public List<SeatMovementAnnouncement> Released { get; } = [];

    public List<SeatMovementAnnouncement> Confirmed { get; } = [];

    public Task SeatsHeldAsync(Guid flightId, IReadOnlyList<Guid> seatIds, DateTimeOffset occurredAt, CancellationToken cancellationToken = default)
    {
        Held.Add(new SeatMovementAnnouncement(flightId, [.. seatIds], occurredAt));
        return Task.CompletedTask;
    }

    public Task SeatsReleasedAsync(Guid flightId, IReadOnlyList<Guid> seatIds, DateTimeOffset occurredAt, CancellationToken cancellationToken = default)
    {
        Released.Add(new SeatMovementAnnouncement(flightId, [.. seatIds], occurredAt));
        return Task.CompletedTask;
    }

    public Task SeatsConfirmedAsync(Guid flightId, IReadOnlyList<Guid> seatIds, DateTimeOffset occurredAt, CancellationToken cancellationToken = default)
    {
        Confirmed.Add(new SeatMovementAnnouncement(flightId, [.. seatIds], occurredAt));
        return Task.CompletedTask;
    }
}

internal sealed record SeatMovementAnnouncement(Guid FlightId, IReadOnlyList<Guid> SeatIds, DateTimeOffset OccurredAt);
