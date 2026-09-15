using FlashFlights.Catalog.Domain;
using FlashFlights.Catalog.Persistence;
using FlashFlights.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FlashFlights.Catalog.Sales;

/// <summary>What one pass over the catalog did, for the scheduler's log.</summary>
/// <param name="Announced">Flights whose crossing was announced on this run.</param>
public sealed record AnnounceSaleStartsResult(int Announced);

/// <summary>
/// Detects Flights crossing their SaleStartsAt and announces each exactly once
/// (spec's third seam, Catalog's half). The one place a sale start becomes an
/// event; <see cref="SaleStartScheduler"/> is only the timer that calls it.
///
/// <para>
/// Exactly-once rests on <see cref="Flight.SaleStartHandledAt"/>: a Flight whose
/// crossing has been settled is never looked at again, so the announcement does
/// not repeat however often the scheduler ticks. The marker is written
/// <em>after</em> a successful publish, so the failure mode is a repeat rather
/// than a silence — Notifications' inbox is unique per (User, Flight), so a
/// duplicate announcement costs nothing, while a dropped one costs every watcher
/// the alert they asked for.
/// </para>
///
/// <para>
/// Every crossing is announced, including one whose window had already closed by
/// the time it was seen — an already-ended sale in the seed data, or a service
/// that was down across the whole window. Catalog does not decide who hears
/// about it: the announcement carries <c>SaleEndsAt</c>, and Notifications is
/// the one that knows a closed window means "tell no one" while still recording
/// that this sale's moment has passed. Judging it here instead would leave
/// Notifications unable to tell an ended sale from one that has not opened, and
/// so willing to accept a Watch that could never fire.
/// </para>
/// </summary>
public sealed class SaleStartAnnouncer(
    CatalogDbContext db,
    IFlightSaleNotifier notifier,
    TimeProvider clock,
    ILogger<SaleStartAnnouncer> logger)
{
    public async Task<AnnounceSaleStartsResult> AnnounceStartedSalesAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();

        // Unsettled crossings only. The window comparison is deliberately not in
        // the query: SQLite has no native DateTimeOffset, EF stores it as text,
        // and the provider refuses to translate a comparison on it — the same
        // caveat Flight.FlashPrice carries. So the marker narrows in SQL and the
        // clock is applied in memory. Unindexed and a full scan of the unsettled
        // rows, which is fine only because the catalog is a handful of flash
        // sales; a real one would want an index on the marker.
        var unsettled = await db.Flights
            .Where(flight => flight.SaleStartHandledAt == null)
            .ToListAsync(cancellationToken);

        var crossed = unsettled
            .Where(flight => flight.SaleStartsAt <= now)
            .OrderBy(flight => flight.SaleStartsAt)
            .ToList();

        var announced = 0;

        foreach (var flight in crossed)
        {
            try
            {
                await notifier.SaleStartedAsync(
                    new FlightSaleStarted(
                        flight.Id,
                        flight.FlightNumber,
                        flight.Origin,
                        flight.Destination,
                        flight.SaleEndsAt,
                        now),
                    cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Left unhandled on purpose: the next tick finds this Flight
                // again, and a watcher is told late rather than not at all. One
                // Flight's failure must not cost the others in this run.
                logger.LogWarning(
                    exception,
                    "Failed to announce the sale start for flight {FlightNumber} ({FlightId}); retrying on the next run.",
                    flight.FlightNumber,
                    flight.Id);

                continue;
            }

            flight.SaleStartHandledAt = now;
            announced++;
        }

        // One save for the whole run: every marker written here has already been
        // announced, and a Flight left unmarked is simply picked up next time.
        await db.SaveChangesAsync(cancellationToken);

        return new AnnounceSaleStartsResult(announced);
    }
}
