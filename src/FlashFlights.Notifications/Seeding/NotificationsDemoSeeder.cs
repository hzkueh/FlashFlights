using FlashFlights.DemoData;
using FlashFlights.Notifications.Domain;
using FlashFlights.Notifications.Persistence;
using FlashFlights.Notifications.Watching;
using Microsoft.EntityFrameworkCore;

namespace FlashFlights.Notifications.Seeding;

/// <summary>
/// Notifications' share of the demo world: the Watches demo buyers are holding,
/// and the Notifications the ones that have already fired left in their inboxes.
///
/// <para>
/// The Notifications are written rather than replayed. Catalog announces every seeded
/// crossing on its first scan, closed windows included, so the sales that opened
/// before the seed <em>are</em> announced here — but an announcement whose
/// window has closed notifies nobody (ADR-0002), and one whose window is open
/// cannot un-ring the bell for a Watch that fired hours ago. Writing them is
/// what makes the inbox look like the history the seeded Watches imply, and the
/// dispatcher's per-(User, Flight) uniqueness is what stops the announcement
/// that follows adding a second copy.
/// </para>
///
/// <para>
/// No <c>SaleAnnouncement</c> is seeded, for the same reason Ordering seeds none:
/// this service learns which sales have opened from the announcement, and
/// writing that down here would hide a broken announcement path.
/// </para>
/// </summary>
public sealed class NotificationsDemoSeeder(NotificationsDbContext db) : IDemoSeeder
{
    public async Task<bool> SeedAsync(DemoWorld world, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(world);

        // Anything a buyer owns here. Announcements are excluded on purpose: one
        // can land before this runs, and an empty inbox under a recorded
        // announcement is an ordinary state rather than a seeded store.
        if (await db.Watches.AnyAsync(cancellationToken) || await db.Notifications.AnyAsync(cancellationToken))
        {
            return false;
        }

        db.Watches.AddRange(world.Watches.Select(watch => new Watch
        {
            Id = watch.Id,
            UserId = watch.Watcher.Id,
            FlightId = watch.Flight.Id,
            CreatedAt = watch.CreatedAt,
        }));

        db.Notifications.AddRange(world.Notifications.Select(notification => new Notification
        {
            Id = notification.Id,
            UserId = notification.Recipient.Id,
            FlightId = notification.Flight.Id,

            // The dispatcher's sentence, not a copy of it, so a seeded Notification and
            // a live one are the same text.
            Body = SaleStartedNotification.Body(
                notification.Flight.FlightNumber,
                notification.Flight.Origin,
                notification.Flight.Destination),
            CreatedAt = notification.CreatedAt,
            ReadAt = notification.ReadAt,
        }));

        // One save, so a failure partway leaves no Watch behind for the retry to
        // read as an already-seeded inbox.
        await db.SaveChangesAsync(cancellationToken);

        return true;
    }
}
