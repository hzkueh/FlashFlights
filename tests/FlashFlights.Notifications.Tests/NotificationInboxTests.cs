using FlashFlights.Notifications.Watching;

namespace FlashFlights.Notifications.Tests;

/// <summary>
/// The persisted inbox (ticket 09, items 5 and 6): what a User sees on their next
/// visit whether or not they were connected when the alert fired, how many of
/// them are unread, and marking one read.
/// </summary>
public class NotificationInboxTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// The inbox is why a Notification survives being missed: nothing about
    /// reading it depends on having been connected when it was created.
    /// </summary>
    [Fact]
    public async Task Notifications_are_listed_newest_first_with_the_unread_count()
    {
        using var testDb = NotificationsTestDb.Create();
        var userId = Guid.NewGuid();
        await testDb.NotifyAsync(userId, "FF001 LHR to BCN is now on sale", createdAt: Now.AddMinutes(-10));
        await testDb.NotifyAsync(userId, "FF002 LHR to CDG is now on sale", createdAt: Now.AddMinutes(-1));

        var inbox = await InboxFor(testDb).ListAsync(userId);

        Assert.Equal(
            ["FF002 LHR to CDG is now on sale", "FF001 LHR to BCN is now on sale"],
            inbox.Notifications.Select(notification => notification.Body));
        Assert.Equal(2, inbox.UnreadCount);
    }

    [Fact]
    public async Task An_empty_inbox_reads_as_empty_rather_than_missing()
    {
        using var testDb = NotificationsTestDb.Create();

        var inbox = await InboxFor(testDb).ListAsync(Guid.NewGuid());

        Assert.Empty(inbox.Notifications);
        Assert.Equal(0, inbox.UnreadCount);
    }

    [Fact]
    public async Task Marking_one_read_stamps_it_and_drops_the_unread_count()
    {
        using var testDb = NotificationsTestDb.Create();
        var userId = Guid.NewGuid();
        var notificationId = await testDb.NotifyAsync(userId, "FF412 LHR to BCN is now on sale");

        Assert.True(await InboxFor(testDb).MarkReadAsync(userId, notificationId));

        var inbox = await InboxFor(testDb).ListAsync(userId);
        Assert.Equal(Now, Assert.Single(inbox.Notifications).ReadAt);
        Assert.Equal(0, inbox.UnreadCount);
    }

    /// <summary>
    /// Read is a state, not an event: marking an already-read Notification read
    /// again succeeds and leaves the original moment it was read alone.
    /// </summary>
    [Fact]
    public async Task Marking_an_already_read_notification_read_keeps_the_first_moment()
    {
        using var testDb = NotificationsTestDb.Create();
        var userId = Guid.NewGuid();
        var readAt = Now.AddMinutes(-5);
        var notificationId = await testDb.NotifyAsync(userId, "FF412 LHR to BCN is now on sale", readAt: readAt);

        Assert.True(await InboxFor(testDb).MarkReadAsync(userId, notificationId));

        var inbox = await InboxFor(testDb).ListAsync(userId);
        Assert.Equal(readAt, Assert.Single(inbox.Notifications).ReadAt);
    }

    /// <summary>
    /// Scoped to the token's User, like Booking history: someone else's
    /// Notification is not found rather than refused, so the endpoint above
    /// cannot be used to discover that it exists.
    /// </summary>
    [Fact]
    public async Task One_user_cannot_read_or_mark_anothers_notification()
    {
        using var testDb = NotificationsTestDb.Create();
        var owner = Guid.NewGuid();
        var stranger = Guid.NewGuid();
        var notificationId = await testDb.NotifyAsync(owner, "FF412 LHR to BCN is now on sale");

        Assert.False(await InboxFor(testDb).MarkReadAsync(stranger, notificationId));

        Assert.Empty((await InboxFor(testDb).ListAsync(stranger)).Notifications);
        Assert.Null(Assert.Single(await testDb.InboxAsync(owner)).ReadAt);
    }

    [Fact]
    public async Task Marking_a_notification_that_does_not_exist_reports_not_found()
    {
        using var testDb = NotificationsTestDb.Create();

        Assert.False(await InboxFor(testDb).MarkReadAsync(Guid.NewGuid(), Guid.NewGuid()));
    }

    private static NotificationInbox InboxFor(NotificationsTestDb testDb) =>
        new(testDb.NewContext(), new FixedClock(Now));

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
