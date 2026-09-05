using FlashFlights.Catalog.Domain;
using FlashFlights.Catalog.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FlashFlights.Catalog.Browsing;

/// <summary>
/// Serves the browse pages from Catalog's own store: Flight metadata joined to
/// the advisory SeatCounts projection, with each sale window's state computed
/// against the shared clock. It never reads Ordering — the counts it hands back
/// are the eventually-consistent projection the consumers maintain, which is the
/// whole point of keeping browsing off Ordering's hot path (ADR-0001).
/// </summary>
public sealed class FlightCatalogService(CatalogDbContext db, TimeProvider clock) : IFlightCatalogService
{
    public async Task<IReadOnlyList<FlightView>> ListFlightsAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();

        // The whole catalog is small (a handful of flash sales), and SaleState is
        // computed rather than stored, so the ordering is decided in memory rather
        // than in a query that cannot see it.
        var flights = await db.Flights
            .Include(flight => flight.SeatCounts)
            .ToListAsync(cancellationToken);

        return flights
            .Select(flight => ToView(flight, now))
            .OrderBy(view => SaleStateRank(view.SaleState))
            .ThenBy(view => OrderingKeyWithin(view))
            .ToArray();
    }

    public async Task<FlightView?> GetFlightAsync(Guid flightId, CancellationToken cancellationToken = default)
    {
        var flight = await db.Flights
            .Include(f => f.SeatCounts)
            .FirstOrDefaultAsync(f => f.Id == flightId, cancellationToken);

        return flight is null ? null : ToView(flight, clock.GetUtcNow());
    }

    private static FlightView ToView(Flight flight, DateTimeOffset now) =>
        new(
            flight.Id,
            flight.FlightNumber,
            flight.Origin,
            flight.Destination,
            flight.DepartureAt,
            flight.FlashPrice,
            flight.ReferenceFare,
            flight.SaleStartsAt,
            flight.SaleEndsAt,
            StateOf(flight, now),
            flight.SeatCounts is { } counts
                ? new SeatCountsView(counts.TotalSeats, counts.AvailableSeats, counts.HeldSeats, counts.ConfirmedSeats)
                : null);

    /// <summary>
    /// The sale's state on the same inclusive boundaries the seat map uses: at the
    /// instant a window opens it is Live, and at the instant it closes it is Ended.
    /// </summary>
    private static SaleState StateOf(Flight flight, DateTimeOffset now)
    {
        if (now < flight.SaleStartsAt)
        {
            return SaleState.Upcoming;
        }

        return now < flight.SaleEndsAt ? SaleState.Live : SaleState.Ended;
    }

    private static int SaleStateRank(SaleState state) => state switch
    {
        SaleState.Live => 0,
        SaleState.Upcoming => 1,
        SaleState.Ended => 2,
        _ => 3,
    };

    /// <summary>
    /// Within a state, an ascending key that reads as "most relevant first": a
    /// live sale by when it ends (most urgent first), an upcoming one by when it
    /// opens (soonest first), an ended one by when it closed but negated, so the
    /// most recently ended leads the tail of the list. The keys only need to sort
    /// within one state — <see cref="SaleStateRank"/> has already separated them.
    /// </summary>
    private static long OrderingKeyWithin(FlightView view) => view.SaleState switch
    {
        SaleState.Live => view.SaleEndsAt.UtcTicks,
        SaleState.Upcoming => view.SaleStartsAt.UtcTicks,
        SaleState.Ended => -view.SaleEndsAt.UtcTicks,
        _ => view.SaleStartsAt.UtcTicks,
    };
}
