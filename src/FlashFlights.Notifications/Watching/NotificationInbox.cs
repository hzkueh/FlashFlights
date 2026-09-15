using FlashFlights.Notifications.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FlashFlights.Notifications.Watching;

/// <summary>
/// The persisted inbox: what a User sees on their next visit whether or not they
/// were connected when an alert fired, and marking one read.
///
/// <para>
/// Scoped to the token's User everywhere, like Booking history — the User is
/// never a body or query field, so a caller cannot read or touch someone else's
/// inbox. A Notification belonging to another User reads as absent rather than
/// refused, so this cannot be used to learn that it exists.
/// </para>
/// </summary>
public interface INotificationInbox
{
    /// <summary>This User's Notifications, newest first, with the unread tally.</summary>
    Task<InboxView> ListAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks one of this User's Notifications read. True when it was theirs to
    /// mark; false when there is no such Notification for this User. Idempotent
    /// — read is a state, so marking an already-read one keeps the moment it was
    /// first read rather than moving it.
    /// </summary>
    Task<bool> MarkReadAsync(Guid userId, Guid notificationId, CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="INotificationInbox"/>
public sealed class NotificationInbox(NotificationsDbContext db, TimeProvider clock) : INotificationInbox
{
    public async Task<InboxView> ListAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        // The inbox is a handful of alerts per User, so it is read whole rather
        // than paged — and the unread count comes off the same rows instead of a
        // second query that could disagree with the list beside it.
        //
        // Newest-first is applied after the read, not in SQL: SQLite has no
        // native DateTimeOffset, EF stores it as text, and the provider refuses
        // to order on it. The index on (UserId, CreatedAt) still does the work
        // that matters here — narrowing to this User's rows.
        var notifications = await db.Notifications
            .Where(notification => notification.UserId == userId)
            .Select(notification => new NotificationView(
                notification.Id,
                notification.FlightId,
                notification.Body,
                notification.CreatedAt,
                notification.ReadAt))
            .ToListAsync(cancellationToken);

        var newestFirst = notifications
            .OrderByDescending(notification => notification.CreatedAt)
            .ToList();

        return new InboxView(newestFirst, newestFirst.Count(notification => notification.ReadAt is null));
    }

    public async Task<bool> MarkReadAsync(
        Guid userId,
        Guid notificationId,
        CancellationToken cancellationToken = default)
    {
        var notification = await db.Notifications.FirstOrDefaultAsync(
            candidate => candidate.Id == notificationId && candidate.UserId == userId,
            cancellationToken);

        if (notification is null)
        {
            return false;
        }

        if (notification.ReadAt is not null)
        {
            return true;
        }

        notification.ReadAt = clock.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);

        return true;
    }
}
