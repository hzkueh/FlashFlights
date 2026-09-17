using FlashFlights.Contracts;
using FlashFlights.Notifications.Domain;
using FlashFlights.Notifications.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FlashFlights.Notifications.Watching;

/// <summary>
/// The spec's third seam: given Catalog's announcement that a sale has opened,
/// find the Watches that match, create a Notification for each watching User,
/// and push it to them. The one place a Watch becomes a Notification.
/// </summary>
public interface IWatchNotificationDispatcher
{
    /// <summary>
    /// Handles one <see cref="FlightSaleStarted"/> and returns how many
    /// Notifications it created. Idempotent: a redelivered announcement creates
    /// nothing and pushes nothing.
    ///
    /// <para>
    /// An announcement whose window has already closed — an already-ended sale in
    /// the seed data, or this service having been down across the whole window —
    /// notifies no one: "now on sale" would be false by the time anyone read it.
    /// It is still recorded, because the sale's one moment has demonstrably
    /// passed and a Watch created afterwards could never fire.
    /// </para>
    /// </summary>
    Task<int> OnFlightSaleStartedAsync(
        FlightSaleStarted announcement,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IWatchNotificationDispatcher"/>
public sealed class WatchNotificationDispatcher(
    NotificationsDbContext db,
    INotificationPusher pusher,
    TimeProvider clock,
    ILogger<WatchNotificationDispatcher> logger) : IWatchNotificationDispatcher
{
    public async Task<int> OnFlightSaleStartedAsync(
        FlightSaleStarted announcement,
        CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();

        // Recorded first and unconditionally: whether or not anyone is told, this
        // Flight's sale has opened, and that is what makes a later Watch on it
        // refusable.
        await RecordAnnouncementAsync(announcement.FlightId, now, cancellationToken);

        if (now >= announcement.SaleEndsAt)
        {
            logger.LogInformation(
                "Flight {FlightNumber} ({FlightId}) opened and closed its sale window unseen; recorded without notifying anyone.",
                announcement.FlightNumber,
                announcement.FlightId);

            return 0;
        }

        var watchers = await db.Watches
            .Where(watch => watch.FlightId == announcement.FlightId)
            .Select(watch => watch.UserId)
            .ToListAsync(cancellationToken);

        if (watchers.Count == 0)
        {
            return 0;
        }

        // Who already holds this Flight's alert. Catalog marks a Flight announced
        // only after a successful publish, so a crash in between re-announces —
        // this is what makes the second delivery a no-op rather than a second
        // buzz. The unique (UserId, FlightId) index is the backstop underneath it.
        var alreadyNotified = await db.Notifications
            .Where(notification => notification.FlightId == announcement.FlightId)
            .Select(notification => notification.UserId)
            .ToListAsync(cancellationToken);

        var newWatchers = watchers.Except(alreadyNotified).ToList();

        if (newWatchers.Count == 0)
        {
            return 0;
        }

        var body = BodyFor(announcement);

        var created = newWatchers
            .Select(userId => new Notification
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                FlightId = announcement.FlightId,
                Body = body,
                CreatedAt = now,
            })
            .ToList();

        db.Notifications.AddRange(created);

        // Persisted before pushed, deliberately. The row is the promise the
        // ticket makes — visible on the next sign-in whether or not the User was
        // connected — and the push is only how a connected User hears it sooner.
        await db.SaveChangesAsync(cancellationToken);

        foreach (var notification in created)
        {
            await PushSafelyAsync(notification, cancellationToken);
        }

        return created.Count;
    }

    /// <summary>
    /// Notes that this Flight's sale has opened, so a Watch created from here on
    /// is refused rather than accepted into a moment that has already passed.
    /// </summary>
    private async Task RecordAnnouncementAsync(
        Guid flightId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var recorded = await db.SaleAnnouncements
            .AnyAsync(announcement => announcement.FlightId == flightId, cancellationToken);

        if (recorded)
        {
            return;
        }

        db.SaleAnnouncements.Add(new SaleAnnouncement { FlightId = flightId, AnnouncedAt = now });
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// A push that fails must not cost the User the inbox row behind it: the
    /// Notification is already durable, so the alert is waiting for them on their
    /// next visit either way. This is the same advisory trade the live seat map
    /// makes — the push is the fast path, never the record.
    /// </summary>
    private async Task PushSafelyAsync(Notification notification, CancellationToken cancellationToken)
    {
        try
        {
            await pusher.NotificationCreatedAsync(
                notification.UserId,
                new NotificationView(
                    notification.Id,
                    notification.FlightId,
                    notification.Body,
                    notification.CreatedAt,
                    notification.ReadAt),
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(
                exception,
                "Failed to push notification {NotificationId}; it is stored and will be read from the inbox instead.",
                notification.Id);
        }
    }

    /// <summary>
    /// What the User reads. Composed from the announcement alone — Notifications
    /// takes no dependency on Catalog, so everything in this sentence had to
    /// travel on the event.
    /// </summary>
    private static string BodyFor(FlightSaleStarted announcement) =>
        SaleStartedNotification.Body(announcement.FlightNumber, announcement.Origin, announcement.Destination);
}
