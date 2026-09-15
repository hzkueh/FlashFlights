using FlashFlights.Notifications.SeatMaps;
using Microsoft.AspNetCore.SignalR;

namespace FlashFlights.Notifications.Tests;

/// <summary>
/// The SignalR edge of the live seat map, and the proof of ticket 08's
/// per-flight rule: a Flight's changes are sent to that Flight's group alone, so
/// a viewer watching one Flight is never pushed another's. Exercised against a
/// fake <see cref="IHubContext{THub}"/> that records what group was addressed and
/// what was sent — the same fake-hub-context shape the spec calls for.
/// </summary>
public class SignalRSeatMapBroadcasterTests
{
    private static readonly DateTimeOffset OccurredAt = new(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Sends_changes_to_that_flights_group_only()
    {
        var flightId = Guid.NewGuid();
        var hub = new RecordingHubContext();

        var seats = new[] { new SeatChange(Guid.NewGuid(), LiveSeatStatus.Held) };
        await new SignalRSeatMapBroadcaster(hub).SeatsChangedAsync(flightId, seats, OccurredAt);

        Assert.Equal(SeatMapHub.GroupFor(flightId), hub.AddressedGroup);
        Assert.Equal(SeatMapHub.SeatsChangedMethod, hub.SentMethod);
    }

    [Fact]
    public async Task Carries_the_flight_seats_and_timestamp_through_unchanged()
    {
        var flightId = Guid.NewGuid();
        var hub = new RecordingHubContext();

        var seats = new[]
        {
            new SeatChange(Guid.NewGuid(), LiveSeatStatus.Confirmed),
            new SeatChange(Guid.NewGuid(), LiveSeatStatus.Confirmed),
        };
        await new SignalRSeatMapBroadcaster(hub).SeatsChangedAsync(flightId, seats, OccurredAt);

        var payload = Assert.IsType<SeatMapChanged>(Assert.Single(hub.SentArgs));
        Assert.Equal(flightId, payload.FlightId);
        Assert.Equal(OccurredAt, payload.OccurredAt);
        Assert.Equal(seats, payload.Seats);
    }

    [Fact]
    public async Task Groups_are_distinct_per_flight()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        Assert.NotEqual(SeatMapHub.GroupFor(first), SeatMapHub.GroupFor(second));
        await Task.CompletedTask;
    }

    /// <summary>
    /// A minimal <see cref="IHubContext{THub}"/> that records the group addressed
    /// via <c>Clients.Group(...)</c> and the method and args sent to it. Only the
    /// members the broadcaster touches are implemented; the rest throw so an
    /// unexpected path fails loudly rather than passing silently.
    /// </summary>
    private sealed class RecordingHubContext : IHubContext<SeatMapHub>
    {
        public string? AddressedGroup { get; private set; }
        public string? SentMethod { get; private set; }
        public IReadOnlyList<object?> SentArgs { get; private set; } = [];

        public IHubClients Clients => new RecordingClients(this);
        public IGroupManager Groups => throw new NotSupportedException();

        private sealed class RecordingClients(RecordingHubContext owner) : IHubClients
        {
            public IClientProxy Group(string groupName)
            {
                owner.AddressedGroup = groupName;
                return new RecordingClientProxy(owner);
            }

            public IClientProxy All => throw new NotSupportedException();
            public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();
            public IClientProxy Client(string connectionId) => throw new NotSupportedException();
            public IClientProxy Clients(IReadOnlyList<string> connectionIds) => throw new NotSupportedException();
            public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();
            public IClientProxy Groups(IReadOnlyList<string> groupNames) => throw new NotSupportedException();
            public IClientProxy User(string userId) => throw new NotSupportedException();
            public IClientProxy Users(IReadOnlyList<string> userIds) => throw new NotSupportedException();
        }

        private sealed class RecordingClientProxy(RecordingHubContext owner) : IClientProxy
        {
            public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
            {
                owner.SentMethod = method;
                owner.SentArgs = args;
                return Task.CompletedTask;
            }
        }
    }
}
