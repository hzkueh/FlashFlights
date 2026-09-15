using FlashFlights.Notifications.Domain;
using FlashFlights.Notifications.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FlashFlights.Notifications.Watching;

/// <summary>
/// Watching and un-watching a Flight, for the one User the token names. The
/// write side of a Watch; <see cref="IWatchNotificationDispatcher"/> is what
/// later reads them.
///
/// <para>
/// A Watch says nothing about whether the Flight exists — Catalog owns Flights,
/// and this service never asks it. A Watch on an id that is not a Flight simply
/// never fires, which costs one row and no correctness; taking a synchronous
/// dependency on Catalog to prevent it would cost this service its independence
/// and make watching fail whenever Catalog is down.
/// </para>
/// </summary>
public interface IWatchService
{
    /// <summary>
    /// Records that this User wants to be told when the Flight's sale opens.
    /// Idempotent: watching twice is the same subscription. Refused once the
    /// sale has already been announced, since a Watch fires only on the window
    /// opening and that moment has passed.
    /// </summary>
    Task<WatchOutcome> WatchAsync(Guid userId, Guid flightId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops future Notifications for this Flight. A no-op when there was no
    /// Watch — "stop notifying me" is the same request either way.
    /// </summary>
    Task UnwatchAsync(Guid userId, Guid flightId, CancellationToken cancellationToken = default);

    /// <summary>The Flights this User is watching, so the SPA can render the toggle already on.</summary>
    Task<IReadOnlyList<Guid>> ListWatchedFlightIdsAsync(Guid userId, CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IWatchService"/>
public sealed class WatchService(NotificationsDbContext db, TimeProvider clock) : IWatchService
{
    public async Task<WatchOutcome> WatchAsync(
        Guid userId,
        Guid flightId,
        CancellationToken cancellationToken = default)
    {
        // The sale's start is a moment this service has either seen pass or not.
        // Seeing it recorded is the whole reason SaleAnnouncement exists.
        var announced = await db.SaleAnnouncements
            .AnyAsync(announcement => announcement.FlightId == flightId, cancellationToken);

        if (announced)
        {
            return WatchOutcome.SaleAlreadyStarted;
        }

        var alreadyWatching = await db.Watches
            .AnyAsync(watch => watch.UserId == userId && watch.FlightId == flightId, cancellationToken);

        if (alreadyWatching)
        {
            return WatchOutcome.Watched;
        }

        db.Watches.Add(new Watch
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            FlightId = flightId,
            CreatedAt = clock.GetUtcNow(),
        });

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Two requests for the same Watch raced past the check above and the
            // unique index caught the loser. Both callers asked for the same
            // thing and both now have it, so this is success, not a failure to
            // report — the store's guarantee is what makes the check advisory.
            return WatchOutcome.Watched;
        }

        return WatchOutcome.Watched;
    }

    public async Task UnwatchAsync(Guid userId, Guid flightId, CancellationToken cancellationToken = default)
    {
        // Deleted, not flagged: there is no watch history worth keeping, and a
        // row that is gone cannot be matched by a later announcement — which is
        // exactly what "stop notifying me" has to mean.
        await db.Watches
            .Where(watch => watch.UserId == userId && watch.FlightId == flightId)
            .ExecuteDeleteAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> ListWatchedFlightIdsAsync(
        Guid userId,
        CancellationToken cancellationToken = default) =>
        await db.Watches
            .Where(watch => watch.UserId == userId)
            .Select(watch => watch.FlightId)
            .ToListAsync(cancellationToken);
}
