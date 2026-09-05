namespace FlashFlights.Catalog.Browsing;

/// <summary>
/// Where a Flight's flash sale is in its one window, as of the moment it was
/// read. Computed from SaleStartsAt/SaleEndsAt against the clock, never stored —
/// a sale does not "become" live, it simply is once its window opens.
/// </summary>
public enum SaleState
{
    /// <summary>The window has not opened yet — SaleStartsAt is still ahead.</summary>
    Upcoming,

    /// <summary>The window is open now — between SaleStartsAt (inclusive) and SaleEndsAt.</summary>
    Live,

    /// <summary>The window has closed — SaleEndsAt has passed.</summary>
    Ended,
}

/// <summary>
/// A Flight's advisory seat tally for the list page (CONTEXT.md's SeatCounts),
/// carried only when Catalog has a projection row for the Flight. Null means the
/// counts are not known yet — the list renders that as "—" rather than "0 left",
/// which would wrongly read as sold out.
/// </summary>
public sealed record SeatCountsView(int Total, int Available, int Held, int Confirmed);

/// <summary>
/// One Flight as the catalog serves it: its route, departure, FlashPrice, sale
/// window, the window's current <see cref="SaleState"/>, and its advisory
/// <see cref="SeatCounts"/>. The raw SaleStartsAt/SaleEndsAt travel alongside the
/// computed state so the SPA can run its own live countdown without asking the
/// server again each second.
///
/// No seat map here: the map is per-Seat status, which Catalog does not hold and
/// the detail page reads live from Ordering instead (spec, ADR-0001).
/// </summary>
public sealed record FlightView(
    Guid Id,
    string FlightNumber,
    string Origin,
    string Destination,
    DateTimeOffset DepartureAt,
    decimal FlashPrice,
    DateTimeOffset SaleStartsAt,
    DateTimeOffset SaleEndsAt,
    SaleState SaleState,
    SeatCountsView? SeatCounts);
