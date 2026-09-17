using System.Security.Claims;
using FlashFlights.Contracts;
using FlashFlights.Notifications.Watching;
using FlashFlights.ServiceDefaults.Authentication;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;

namespace FlashFlights.Notifications.Tests;

/// <summary>
/// The SignalR edge of the watch feature, and the proof of its per-User rule: an
/// alert is sent to the User it belongs to and to no one else. Exercised against
/// a fake <see cref="IHubContext{THub}"/> that records who was addressed and what
/// was sent — the shape the spec calls for.
/// </summary>
public class NotificationPushTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Sends_the_alert_to_its_own_user_only()
    {
        var userId = Guid.NewGuid();
        var hub = new RecordingHubContext();

        await new SignalRNotificationPusher(hub).NotificationCreatedAsync(userId, ANotification(userId));

        Assert.Equal(userId.ToString(), hub.AddressedUser);
        Assert.Equal(NotificationsHub.NotificationReceivedMethod, hub.SentMethod);
    }

    [Fact]
    public async Task Carries_the_whole_notification_through_unchanged()
    {
        var userId = Guid.NewGuid();
        var hub = new RecordingHubContext();
        var notification = ANotification(userId);

        await new SignalRNotificationPusher(hub).NotificationCreatedAsync(userId, notification);

        var payload = Assert.IsType<NotificationView>(Assert.Single(hub.SentArgs));
        Assert.Equal(notification, payload);
    }

    /// <summary>
    /// The one thing about per-User addressing that could break silently: a
    /// connection has to be named the same way the push addresses it, or every
    /// alert would be sent to a User nobody is signed in as and simply vanish.
    /// </summary>
    [Fact]
    public async Task A_connection_is_named_the_same_way_the_push_addresses_it()
    {
        var userId = Guid.NewGuid();
        var hub = new RecordingHubContext();

        await new SignalRNotificationPusher(hub).NotificationCreatedAsync(userId, ANotification(userId));

        Assert.Equal(hub.AddressedUser, FlashFlightsUserIdProvider.UserIdFor(PrincipalFor(userId)));
    }

    /// <summary>
    /// Claims arrive unmapped, so the User's id is the raw <c>sub</c>. A token
    /// carrying only the ClaimTypes URI SignalR looks for by default names no
    /// User here — which is exactly why the provider exists.
    /// </summary>
    [Fact]
    public void An_unauthenticated_connection_names_no_user()
    {
        Assert.Null(FlashFlightsUserIdProvider.UserIdFor(null));
        Assert.Null(FlashFlightsUserIdProvider.UserIdFor(new ClaimsPrincipal(new ClaimsIdentity())));
        Assert.Null(FlashFlightsUserIdProvider.UserIdFor(
            new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())]))));
    }

    /// <summary>
    /// The spec's test target end to end: given a set of Watches and a
    /// FlightSaleStarted, the dispatcher creates the right Notifications and the
    /// real pusher delivers each to its own User's connections — asserted here
    /// against the fake IHubContext rather than a stand-in for it, so nothing
    /// between the Watch and the hub is taken on trust.
    /// </summary>
    [Fact]
    public async Task A_watcher_is_pushed_their_own_alert_all_the_way_to_the_hub()
    {
        using var testDb = NotificationsTestDb.Create();
        var watcher = Guid.NewGuid();
        var bystander = Guid.NewGuid();
        var announcement = new FlightSaleStarted(
            Guid.NewGuid(),
            "FF412",
            "LHR",
            "BCN",
            Now.AddHours(2),
            Now);

        await testDb.WatchAsync(watcher, announcement.FlightId);
        // Watching something else entirely — they must not be addressed at all.
        await testDb.WatchAsync(bystander, Guid.NewGuid());

        var hub = new RecordingHubContext();
        var dispatcher = new WatchNotificationDispatcher(
            testDb.NewContext(),
            new SignalRNotificationPusher(hub),
            new FixedClock(Now),
            NullLogger<WatchNotificationDispatcher>.Instance);

        Assert.Equal(1, await dispatcher.OnFlightSaleStartedAsync(announcement));

        Assert.Equal(watcher.ToString(), hub.AddressedUser);

        var payload = Assert.IsType<NotificationView>(Assert.Single(hub.SentArgs));
        Assert.Equal(announcement.FlightId, payload.FlightId);
        Assert.Contains("FF412", payload.Body);
    }

    private static NotificationView ANotification(Guid userId) =>
        new(Guid.NewGuid(), Guid.NewGuid(), $"FF412 LHR to BCN is now on sale (for {userId:N})", Now, null);

    private static ClaimsPrincipal PrincipalFor(Guid userId) =>
        new(new ClaimsIdentity([new Claim(FlashFlightsClaims.UserId, userId.ToString())], "test"));

    /// <summary>
    /// A minimal <see cref="IHubContext{THub}"/> that records the User addressed
    /// via <c>Clients.User(...)</c> and what was sent to them. Only the members
    /// the pusher touches are implemented; the rest throw, so an unexpected path
    /// fails loudly rather than passing silently.
    /// </summary>
    private sealed class RecordingHubContext : IHubContext<NotificationsHub>
    {
        public string? AddressedUser { get; private set; }
        public string? SentMethod { get; private set; }
        public IReadOnlyList<object?> SentArgs { get; private set; } = [];

        public IHubClients Clients => new RecordingClients(this);
        public IGroupManager Groups => throw new NotSupportedException();

        private sealed class RecordingClients(RecordingHubContext owner) : IHubClients
        {
            public IClientProxy User(string userId)
            {
                owner.AddressedUser = userId;
                return new RecordingClientProxy(owner);
            }

            public IClientProxy All => throw new NotSupportedException();
            public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();
            public IClientProxy Client(string connectionId) => throw new NotSupportedException();
            public IClientProxy Clients(IReadOnlyList<string> connectionIds) => throw new NotSupportedException();
            public IClientProxy Group(string groupName) => throw new NotSupportedException();
            public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();
            public IClientProxy Groups(IReadOnlyList<string> groupNames) => throw new NotSupportedException();
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
