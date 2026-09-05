namespace FlashFlights.Catalog.Browsing;

/// <summary>
/// Catalog's browse seam (spec's second seam): the flights running flash sales,
/// and any one of them by id. Read-only and unauthenticated — a visitor browses
/// the catalog without signing in.
///
/// What it returns is Flight metadata plus the advisory SeatCounts projection,
/// never a per-Seat status: the seat map is Ordering's to serve. These two
/// pages read from two places on purpose (ADR-0001), which is why the list's
/// remaining-seats figure and a flight's own seat map can briefly disagree.
/// </summary>
public interface IFlightCatalogService
{
    /// <summary>
    /// Every Flight, ordered the way a buyer wants to meet them: live sales
    /// first (soonest to end, so most urgent), then upcoming (soonest to open),
    /// then ended (most recently closed).
    /// </summary>
    Task<IReadOnlyList<FlightView>> ListFlightsAsync(CancellationToken cancellationToken = default);

    /// <summary>One Flight by id, or null when no such Flight exists.</summary>
    Task<FlightView?> GetFlightAsync(Guid flightId, CancellationToken cancellationToken = default);
}
