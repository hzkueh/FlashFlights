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

    /// <summary>
    /// The standing, non-sale price this Flight's FlashPrice is marked down from,
    /// or null when no saving is advertised (CONTEXT.md's ReferenceFare). Display
    /// only: never charged, never the price paid, never the basis for a Hold. When
    /// present it must be greater than FlashPrice; the saving percentage is derived
    /// by the SPA, never stored here. Same decimal-as-text SQLite caveat as
    /// FlashPrice — do not compare or order on it in SQL.
    /// </summary>
    public decimal? ReferenceFare { get; set; }

    public DateTimeOffset SaleStartsAt { get; set; }

    public DateTimeOffset SaleEndsAt { get; set; }

    /// <summary>
    /// When the sale-start scheduler settled this Flight's crossing of
    /// <see cref="SaleStartsAt"/>, or null while the crossing is still ahead or
    /// unhandled. Set once and never cleared: it is what makes
    /// <c>FlightSaleStarted</c> fire exactly once per Flight however often the
    /// scheduler runs (ticket 09).
    ///
    /// "Settled", not "announced": a window that had already closed by the time
    /// the crossing was seen is marked here without an announcement, since "now
    /// on sale" would be false by the time anyone read it. See
    /// <see cref="Sales.SaleStartAnnouncer"/>.
    /// </summary>
    public DateTimeOffset? SaleStartHandledAt { get; set; }

    public FlightSeatCounts? SeatCounts { get; set; }
}
