namespace FlashFlights.Catalog.Domain;

/// <summary>
/// The flash-sale unit for sale: a route, a departure, a FlashPrice, and one
/// sale window. One sale per Flight — there is no separate FlashSale entity
/// (CONTEXT.md).
///
/// Catalog owns flight metadata only. The Seats themselves live in Ordering,
/// which is the sole source of truth for their status (ADR-0001); all Catalog
/// keeps of them is the <see cref="SeatCounts"/> projection.
/// </summary>
public sealed class Flight
{
    public Guid Id { get; set; }

    /// <summary>Carrier designator shown to buyers, e.g. "FF412".</summary>
    public required string FlightNumber { get; set; }

    /// <summary>Departure airport code, e.g. "LHR".</summary>
    public required string Origin { get; set; }

    /// <summary>Arrival airport code, e.g. "BCN".</summary>
    public required string Destination { get; set; }

    public DateTimeOffset DepartureAt { get; set; }

    /// <summary>
    /// Price per Seat while the sale is live. Stored as a decimal; note that
    /// SQLite has no native decimal type and EF stores it as text, so this
    /// must not be compared or ordered on in SQL — do it in memory.
    /// </summary>
    public decimal FlashPrice { get; set; }

    public DateTimeOffset SaleStartsAt { get; set; }

    public DateTimeOffset SaleEndsAt { get; set; }

    public FlightSeatCounts? SeatCounts { get; set; }
}
