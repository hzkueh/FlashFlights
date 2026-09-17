using FlashFlights.Contracts;
using FlashFlights.DemoData;
using FlashFlights.Notifications.Seeding;
using FlashFlights.Notifications.Watching;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FlashFlights.Notifications.Tests;

/// <summary>
/// Notifications' half of ticket 11: a demo buyer's inbox and watch list are not
/// empty on a fresh look, the sale that is about to open is being watched, and a
/// restart leaves all of it as it was.
/// </summary>
public class NotificationsDemoSeederTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 17, 9, 0, 0, TimeSpan.Zero);

    private static DemoWorld World() => DemoWorld.Create(Now, new DemoSeedOptions());

    [Fact]
    public async Task Seeds_the_watches_and_notifications_the_demo_world_describes()
    {
        using var testDb = NotificationsTestDb.Create();

        Assert.True(await SeedAsync(testDb));

        var world = World();
        await using var db = testDb.NewContext();

        Assert.Equal(world.Watches.Count, await db.Watches.CountAsync());
        Assert.Equal(world.Notifications.Count, await db.Notifications.CountAsync());
    }

    /// <summary>"The inbox isn't empty on a fresh look" — and it has an unread one in it.</summary>
    [Fact]
    public async Task A_demo_buyer_arrives_with_a_read_notification_and_an_unread_one()
    {
        using var testDb = NotificationsTestDb.Create();
        await SeedAsync(testDb);

        var inbox = await testDb.InboxAsync(DemoUsers.Ada.Id);

        Assert.Contains(inbox, notification => notification.ReadAt is null);
        Assert.Contains(inbox, notification => notification.ReadAt is not null);
    }

    /// <summary>
    /// A seeded Notification has to read exactly as a live one does, or the first real
    /// notification to arrive looks like it came from a different system.
    /// </summary>
    [Fact]
    public async Task A_seeded_notification_reads_the_same_as_one_the_dispatcher_writes()
    {
        var world = World();
        var announced = world.Notifications[0].Flight;

        using var testDb = NotificationsTestDb.Create();
        await SeedAsync(testDb);

        var seeded = (await testDb.InboxAsync(DemoUsers.Ada.Id))
            .Single(notification => notification.FlightId == announced.Id);

        Assert.Equal(
            SaleStartedNotification.Body(announced.FlightNumber, announced.Origin, announced.Destination),
            seeded.Body);
    }

    /// <summary>
    /// The live demo: the Flight whose sale opens moments from now is one a
    /// seeded buyer is waiting on, so the Notification fires while a reviewer watches.
    /// </summary>
    [Fact]
    public async Task The_sale_about_to_open_is_already_being_watched()
    {
        var world = World();
        var imminent = world.Flights.Where(flight => flight.SaleStartsAt > Now).MinBy(flight => flight.SaleStartsAt)!;

        using var testDb = NotificationsTestDb.Create();
        await SeedAsync(testDb);

        await using var db = testDb.NewContext();

        Assert.True(await db.Watches.AnyAsync(watch => watch.FlightId == imminent.Id));
    }

    /// <summary>
    /// The end-to-end shape of the watch demo, driven through the dispatcher that
    /// Catalog's announcement actually reaches.
    /// </summary>
    [Fact]
    public async Task The_watched_sale_opening_notifies_the_buyer_who_was_waiting()
    {
        var world = World();
        var imminent = world.Flights.Where(flight => flight.SaleStartsAt > Now).MinBy(flight => flight.SaleStartsAt)!;

        using var testDb = NotificationsTestDb.Create();
        await SeedAsync(testDb);

        var opened = imminent.SaleStartsAt;
        var pusher = new RecordingNotificationPusher();
        var created = await DispatcherFor(testDb, opened, pusher)
            .OnFlightSaleStartedAsync(AnnouncementFor(imminent, opened));

        Assert.Equal(1, created);
        Assert.Contains(
            await testDb.InboxAsync(DemoUsers.Katherine.Id),
            notification => notification.FlightId == imminent.Id);

        // Pushed as well as stored — this is the half a reviewer actually sees
        // arrive, without reloading anything.
        Assert.Equal(DemoUsers.Katherine.Id, Assert.Single(pusher.Pushes).UserId);
    }

    /// <summary>
    /// Catalog announces every seeded crossing, including the ones that opened
    /// before the seed ran. A seeded Notification must absorb that rather than become a
    /// second buzz for something the buyer was told about hours ago.
    /// </summary>
    [Fact]
    public async Task The_announcement_for_an_already_seeded_notification_adds_nothing()
    {
        var world = World();
        var alreadyAlerted = world.Notifications[0].Flight;

        using var testDb = NotificationsTestDb.Create();
        await SeedAsync(testDb);

        var created = await DispatcherFor(testDb, Now, new RecordingNotificationPusher())
            .OnFlightSaleStartedAsync(AnnouncementFor(alreadyAlerted, alreadyAlerted.SaleStartsAt));

        Assert.Equal(0, created);
        Assert.Single(
            await testDb.InboxAsync(DemoUsers.Ada.Id),
            notification => notification.FlightId == alreadyAlerted.Id);
    }

    [Fact]
    public async Task Does_not_pretend_to_have_heard_an_announcement_it_was_never_sent()
    {
        using var testDb = NotificationsTestDb.Create();
        await SeedAsync(testDb);

        await using var db = testDb.NewContext();

        Assert.Empty(await db.SaleAnnouncements.ToListAsync());
    }

    /// <summary>
    /// An announcement can land before this service seeds. That records the sale
    /// as opened but leaves no Watch and no Notification, which is an empty inbox rather
    /// than a seeded one — so the seed still has to run.
    /// </summary>
    [Fact]
    public async Task Still_seeds_when_an_announcement_arrived_first()
    {
        using var testDb = NotificationsTestDb.Create();
        await testDb.RecordSaleAnnouncedAsync(World().Flights[0].Id);

        Assert.True(await SeedAsync(testDb));
    }

    [Fact]
    public async Task A_second_run_over_a_seeded_store_changes_nothing()
    {
        using var testDb = NotificationsTestDb.Create();
        await SeedAsync(testDb);

        var before = await SnapshotAsync(testDb);

        Assert.False(await SeedAsync(testDb));
        Assert.Equal(before, await SnapshotAsync(testDb));
    }

    [Fact]
    public async Task Leaves_a_store_that_already_has_a_watch_alone()
    {
        using var testDb = NotificationsTestDb.Create();
        await testDb.WatchAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.False(await SeedAsync(testDb));

        await using var db = testDb.NewContext();
        Assert.Equal(1, await db.Watches.CountAsync());
    }

    private static async Task<bool> SeedAsync(NotificationsTestDb testDb)
    {
        await using var db = testDb.NewContext();

        return await new NotificationsDemoSeeder(db).SeedAsync(World());
    }

    private static WatchNotificationDispatcher DispatcherFor(
        NotificationsTestDb testDb,
        DateTimeOffset now,
        INotificationPusher pusher) =>
        new(
            testDb.NewContext(),
            pusher,
            new FixedClock(now),
            NullLogger<WatchNotificationDispatcher>.Instance);

    private static FlightSaleStarted AnnouncementFor(DemoFlight flight, DateTimeOffset announcedAt) =>
        new(flight.Id, flight.FlightNumber, flight.Origin, flight.Destination, flight.SaleEndsAt, announcedAt);

    private static async Task<string> SnapshotAsync(NotificationsTestDb testDb)
    {
        await using var db = testDb.NewContext();

        var watches = await db.Watches.ToListAsync();
        var notifications = await db.Notifications.ToListAsync();

        return string.Join(
            "\n",
            watches.Select(watch => $"watch|{watch.Id}|{watch.UserId}|{watch.FlightId}|{watch.CreatedAt:O}")
                .Concat(notifications.Select(notification =>
                    $"notification|{notification.Id}|{notification.UserId}|{notification.FlightId}"
                    + $"|{notification.Body}|{notification.CreatedAt:O}|{notification.ReadAt:O}"))
                .Order(StringComparer.Ordinal));
    }
}
