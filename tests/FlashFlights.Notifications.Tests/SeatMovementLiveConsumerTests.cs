using System.Collections.Concurrent;
using FlashFlights.Contracts;
using FlashFlights.Notifications.SeatMaps;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace FlashFlights.Notifications.Tests;

/// <summary>
/// The relay seam for ticket 08: a movement Ordering commits and publishes must
/// reach this service's consumer and be pushed on to that Flight's live viewers
/// as the resulting per-Seat status. The consumers are exercised through the bus
/// against a fake broadcaster — the SignalR edge is proven separately; what needs
/// proving here is that the event type is translated to the right status for the
/// right Flight's Seats.
/// </summary>
public class SeatMovementLiveConsumerTests
{
    private static readonly DateTimeOffset OccurredAt = new(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Held_seats_are_pushed_as_Held_to_their_flight()
    {
        var flightId = Guid.NewGuid();
        var seatIds = new[] { Guid.NewGuid(), Guid.NewGuid() };

        var broadcast = await RelaySingle(harness => harness.Bus.Publish(new SeatsHeld(flightId, seatIds, OccurredAt)));

        Assert.Equal(flightId, broadcast.FlightId);
        Assert.Equal(OccurredAt, broadcast.OccurredAt);
        Assert.Equal(seatIds, broadcast.Seats.Select(s => s.SeatId));
        Assert.All(broadcast.Seats, seat => Assert.Equal(LiveSeatStatus.Held, seat.Status));
    }

    [Fact]
    public async Task Released_seats_are_pushed_as_Available()
    {
        var flightId = Guid.NewGuid();
        var seatIds = new[] { Guid.NewGuid() };

        var broadcast = await RelaySingle(harness => harness.Bus.Publish(new SeatsReleased(flightId, seatIds, OccurredAt)));

        Assert.Equal(flightId, broadcast.FlightId);
        Assert.All(broadcast.Seats, seat => Assert.Equal(LiveSeatStatus.Available, seat.Status));
        Assert.Equal(seatIds, broadcast.Seats.Select(s => s.SeatId));
    }

    [Fact]
    public async Task Confirmed_seats_are_pushed_as_Confirmed()
    {
        var flightId = Guid.NewGuid();
        var seatIds = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };

        var broadcast = await RelaySingle(harness => harness.Bus.Publish(new SeatsConfirmed(flightId, seatIds, OccurredAt)));

        Assert.All(broadcast.Seats, seat => Assert.Equal(LiveSeatStatus.Confirmed, seat.Status));
        Assert.Equal(seatIds, broadcast.Seats.Select(s => s.SeatId));
    }

    /// <summary>Publishes via <paramref name="publish"/>, waits for the relay, and returns the single broadcast it produced.</summary>
    private static async Task<SeatMapChanged> RelaySingle(Func<ITestHarness, Task> publish)
    {
        var recorder = new RecordingBroadcaster();

        await using var provider = new ServiceCollection()
            .AddSingleton<ISeatMapBroadcaster>(recorder)
            .AddMassTransitTestHarness(bus =>
            {
                bus.AddConsumer<SeatsHeldLiveConsumer>();
                bus.AddConsumer<SeatsReleasedLiveConsumer>();
                bus.AddConsumer<SeatsConfirmedLiveConsumer>();
            })
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        await publish(harness);

        Assert.True(await harness.Consumed.Any<SeatsHeld>() || await harness.Consumed.Any<SeatsReleased>() || await harness.Consumed.Any<SeatsConfirmed>());

        return await recorder.Single();
    }

    private sealed class RecordingBroadcaster : ISeatMapBroadcaster
    {
        private readonly ConcurrentQueue<SeatMapChanged> broadcasts = new();
        private readonly TaskCompletionSource<SeatMapChanged> first =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task SeatsChangedAsync(Guid flightId, IReadOnlyList<SeatChange> seats, DateTimeOffset occurredAt, CancellationToken cancellationToken = default)
        {
            var change = new SeatMapChanged(flightId, seats, occurredAt);
            broadcasts.Enqueue(change);
            first.TrySetResult(change);
            return Task.CompletedTask;
        }

        public async Task<SeatMapChanged> Single()
        {
            var change = await first.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Single(broadcasts);
            return change;
        }
    }
}
