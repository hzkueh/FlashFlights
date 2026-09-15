using FlashFlights.Contracts;
using FlashFlights.Ordering.Domain;
using FlashFlights.Ordering.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FlashFlights.Ordering.Sales;

/// <summary>
/// How Ordering comes to know a Flight has a sale window: the one place
/// Catalog's announcement becomes a <see cref="SaleAnnouncement"/> row for
/// <see cref="SaleWindowRules"/> to read. The read side of that row lives in
/// <c>HoldService</c>, which is the only thing that asks.
///
/// <para>
/// Ordering consumes the announcement rather than asking Catalog per Hold, for
/// the reason ADR-0002 gives and ADR-0003 restates: a synchronous dependency here
/// would land on the one path this system exists to prove safe under
/// concurrency, and make holding fail whenever Catalog is down.
/// </para>
/// </summary>
public interface ISaleWindowRecorder
{
    /// <summary>
    /// Records the window one announcement carries. Idempotent — the
    /// announcement is published at-least-once, so a redelivery must find the row
    /// already there and change nothing.
    /// </summary>
    Task OnFlightSaleStartedAsync(FlightSaleStarted announcement, CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="ISaleWindowRecorder"/>
public sealed class SaleWindowRecorder(OrderingDbContext db, TimeProvider clock) : ISaleWindowRecorder
{
    public async Task OnFlightSaleStartedAsync(
        FlightSaleStarted announcement,
        CancellationToken cancellationToken = default)
    {
        // First write wins, deliberately. One sale window per Flight (CONTEXT.md),
        // announced once for the life of the system, so a second message naming a
        // different end is a redelivery rather than a window being moved — and
        // overwriting would silently change what every Hold already granted was a
        // claim on.
        var recorded = await db.SaleAnnouncements
            .AnyAsync(existing => existing.FlightId == announcement.FlightId, cancellationToken);

        if (recorded)
        {
            return;
        }

        db.SaleAnnouncements.Add(new SaleAnnouncement
        {
            FlightId = announcement.FlightId,
            SaleEndsAt = announcement.SaleEndsAt,
            AnnouncedAt = clock.GetUtcNow(),
        });

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Two deliveries of the same announcement raced past the check above
            // and the FlightId primary key caught the loser. Both wanted the same
            // row and the row is there, so that is the idempotence working, not a
            // failure to retry — confirmed rather than assumed, since a save can
            // fail for reasons that leave no row behind and swallowing those would
            // leave this Flight unholdable with nothing to show for it.
            db.ChangeTracker.Clear();

            var wonByTheOtherDelivery = await db.SaleAnnouncements
                .AnyAsync(existing => existing.FlightId == announcement.FlightId, cancellationToken);

            if (!wonByTheOtherDelivery)
            {
                throw;
            }
        }
    }
}
