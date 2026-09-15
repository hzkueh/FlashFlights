using System.Collections.Concurrent;
using FlashFlights.Contracts;
using FlashFlights.Notifications.Watching;
using Microsoft.Extensions.Logging.Abstractions;

namespace FlashFlights.Notifications.Tests;

/// <summary>
/// The spec's third test target: given a set of Watches and a
/// <see cref="FlightSaleStarted"/>, the right Notification records are created
/// and pushed. The dispatcher is exercised directly against a recording pusher —
/// the bus consumer above it is a thin wrapper, and the SignalR edge is proven
/// separately.
/// </summary>
public class WatchNotificationDispatcherTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);

    private static readonly FlightSaleStarted SaleStarted = new(
        Guid.NewGuid(),
        "FF412",
        "LHR",
        "BCN",
        Now.AddHours(2),
        Now);

    [Fact]
    public async Task Every_watcher_of_the_flight_gets_a_notification()
    {
        using var testDb = NotificationsTestDb.Create();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        await testDb.WatchAsync(first, SaleStarted.FlightId);
        await testDb.WatchAsync(second, SaleStarted.FlightId);

        var pusher = new RecordingNotificationPusher();
        var created = await DispatcherFor(testDb, pusher).OnFlightSaleStartedAsync(SaleStarted);

        Assert.Equal(2, created);
        Assert.Equal(SaleStarted.FlightId, Assert.Single(await testDb.InboxAsync(first)).FlightId);
        Assert.Single(await testDb.InboxAsync(second));
    }

    /// <summary>
    /// Someone watching a different Flight is not watching this one. The whole
    /// point of a Watch is that it names one Flight.
    /// </summary>
    [Fact]
    public async Task Someone_watching_a_different_flight_is_not_notified()
    {
        using var testDb = NotificationsTestDb.Create();
        var watcher = Guid.NewGuid();
        var bystander = Guid.NewGuid();
        await testDb.WatchAsync(watcher, SaleStarted.FlightId);
        await testDb.WatchAsync(bystander, Guid.NewGuid());

        await DispatcherFor(testDb, new RecordingNotificationPusher()).OnFlightSaleStartedAsync(SaleStarted);

        Assert.Single(await testDb.InboxAsync(watcher));
        Assert.Empty(await testDb.InboxAsync(bystander));
    }

    /// <summary>Un-watching is what "stop notifying me" has to mean (ticket item 7).</summary>
    [Fact]
    public async Task An_unwatched_flight_notifies_no_one()
    {
        using var testDb = NotificationsTestDb.Create();
        var userId = Guid.NewGuid();
        await testDb.WatchAsync(userId, SaleStarted.FlightId);

        await WatchServiceFor(testDb).UnwatchAsync(userId, SaleStarted.FlightId);

        var pusher = new RecordingNotificationPusher();
        var created = await DispatcherFor(testDb, pusher).OnFlightSaleStartedAsync(SaleStarted);

        Assert.Equal(0, created);
        Assert.Empty(await testDb.InboxAsync(userId));
        Assert.Empty(pusher.Pushes);
    }

    /// <summary>
    /// Catalog marks a Flight announced only after a successful publish, so a
    /// crash in between re-announces. The inbox must absorb that — one alert per
    /// (User, Flight) however many times the announcement arrives.
    /// </summary>
    [Fact]
    public async Task A_redelivered_announcement_does_not_duplicate_an_inbox()
    {
        using var testDb = NotificationsTestDb.Create();
        var userId = Guid.NewGuid();
        await testDb.WatchAsync(userId, SaleStarted.FlightId);

        var pusher = new RecordingNotificationPusher();
        await DispatcherFor(testDb, pusher).OnFlightSaleStartedAsync(SaleStarted);
        var second = await DispatcherFor(testDb, pusher).OnFlightSaleStartedAsync(SaleStarted);

        Assert.Equal(0, second);
        Assert.Single(await testDb.InboxAsync(userId));

        // And no second buzz for an alert the User has already been given.
        Assert.Single(pusher.Pushes);
    }

    /// <summary>
    /// Connected or not, the record is written first and pushed second — the push
    /// is how a connected User hears it now, the row is why anyone else still
    /// sees it later (ticket items 4 and 5).
    /// </summary>
    [Fact]
    public async Task The_notification_is_pushed_to_its_own_user_and_persisted()
    {
        using var testDb = NotificationsTestDb.Create();
        var userId = Guid.NewGuid();
        await testDb.WatchAsync(userId, SaleStarted.FlightId);

        var pusher = new RecordingNotificationPusher();
        await DispatcherFor(testDb, pusher).OnFlightSaleStartedAsync(SaleStarted);

        var (pushedTo, pushed) = Assert.Single(pusher.Pushes);
        var stored = Assert.Single(await testDb.InboxAsync(userId));

        Assert.Equal(userId, pushedTo);
        Assert.Equal(stored.Id, pushed.Id);
        Assert.Equal(stored.Body, pushed.Body);
        Assert.Null(pushed.ReadAt);
    }

    /// <summary>
    /// Notifications never reads Catalog, so what the User is told is composed
    /// from the announcement alone.
    /// </summary>
    [Fact]
    public async Task The_body_names_the_flight_and_its_route()
    {
        using var testDb = NotificationsTestDb.Create();
        var userId = Guid.NewGuid();
        await testDb.WatchAsync(userId, SaleStarted.FlightId);

        await DispatcherFor(testDb, new RecordingNotificationPusher()).OnFlightSaleStartedAsync(SaleStarted);

        var stored = Assert.Single(await testDb.InboxAsync(userId));
        Assert.Contains("FF412", stored.Body);
        Assert.Contains("LHR", stored.Body);
        Assert.Contains("BCN", stored.Body);
        Assert.Equal(Now, stored.CreatedAt);
        Assert.Null(stored.ReadAt);
    }

    /// <summary>
    /// A sale that has already opened cannot be watched — so the dispatcher
    /// records the announcement itself, which is how the Watch endpoint knows
    /// without ever asking Catalog.
    /// </summary>
    [Fact]
    public async Task An_announced_sale_can_no_longer_be_watched()
    {
        using var testDb = NotificationsTestDb.Create();

        await DispatcherFor(testDb, new RecordingNotificationPusher()).OnFlightSaleStartedAsync(SaleStarted);

        var result = await WatchServiceFor(testDb).WatchAsync(Guid.NewGuid(), SaleStarted.FlightId);

        Assert.Equal(WatchOutcome.SaleAlreadyStarted, result);
    }

    /// <summary>A push that fails must not cost the User the inbox row behind it.</summary>
    [Fact]
    public async Task A_failed_push_still_leaves_the_notification_in_the_inbox()
    {
        using var testDb = NotificationsTestDb.Create();
        var userId = Guid.NewGuid();
        await testDb.WatchAsync(userId, SaleStarted.FlightId);

        var pusher = new RecordingNotificationPusher { FailEveryPush = true };
        var created = await DispatcherFor(testDb, pusher).OnFlightSaleStartedAsync(SaleStarted);

        Assert.Equal(1, created);
        Assert.Single(await testDb.InboxAsync(userId));
    }

    private static WatchNotificationDispatcher DispatcherFor(
        NotificationsTestDb testDb,
        INotificationPusher pusher) =>
        new(
            testDb.NewContext(),
            pusher,
            new FixedClock(Now),
            NullLogger<WatchNotificationDispatcher>.Instance);

    private static WatchService WatchServiceFor(NotificationsTestDb testDb) =>
        new(testDb.NewContext(), new FixedClock(Now));

    private sealed class RecordingNotificationPusher : INotificationPusher
    {
        private readonly ConcurrentQueue<(Guid UserId, NotificationView Notification)> _pushes = new();

        public bool FailEveryPush { get; init; }

        public IReadOnlyCollection<(Guid UserId, NotificationView Notification)> Pushes => _pushes;

        public Task NotificationCreatedAsync(
            Guid userId,
            NotificationView notification,
            CancellationToken cancellationToken = default)
        {
            if (FailEveryPush)
            {
                return Task.FromException(new InvalidOperationException("No hub here."));
            }

            _pushes.Enqueue((userId, notification));
            return Task.CompletedTask;
        }
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
