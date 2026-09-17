namespace FlashFlights.Ordering.Domain;

/// <summary>
/// That one Flight's sale-start announcement has reached this service, and when
/// its window closes. Not a domain concept a buyer would name — it is how
/// Ordering answers "is this Flight's flash price on offer right now?" without
/// asking Catalog, the same bookkeeping Notifications keeps under the same name
/// (ADR-0002).
///
/// <para>
/// It exists for one rule: a Hold is a claim on a <em>flash price</em>
/// (CONTEXT.md), so it may only be granted while that price is on offer.
/// Ordering learns the window only from the <c>FlightSaleStarted</c> it consumes
/// — the announcement is the sale having opened, and <see cref="SaleEndsAt"/> is
/// when it closes — and takes no dependency on Catalog to ask again, which would
/// put a second service on the one path this system exists to prove safe under
/// concurrency. See <see href="../../../docs/adr/0003-ordering-refuses-a-hold-it-has-no-sale-window-for.md">ADR-0003</see>.
/// </para>
///
/// <para>
/// One row per Flight, keyed by the Flight: the announcement is published
/// at-least-once, so a redelivery must find the row already there and change
/// nothing.
/// </para>
/// </summary>
public sealed class SaleAnnouncement
{
    /// <summary>
    /// Primary key as well as the reference to the Flight — one row per Flight.
    /// Catalog owns Flights; this is a cross-service reference, not an FK.
    /// </summary>
    public Guid FlightId { get; set; }

    /// <summary>
    /// When this Flight's window closes, as the announcement reported it. The
    /// field the Hold rule actually reads — Catalog's clock, not this service's,
    /// but it is a stated instant rather than a measurement, so the two agree.
    /// </summary>
    public DateTimeOffset SaleEndsAt { get; set; }

    /// <summary>
    /// When this service consumed the announcement, by its own clock. Never part
    /// of the rule — the rule reads <see cref="SaleEndsAt"/> — but it is the
    /// answer to the one question a refused Hold raises: did this Flight's
    /// announcement ever reach Ordering, and when? A Flight announced before
    /// Ordering began consuming is never re-announced (ADR-0003), so "no row" and
    /// "a row that landed late" are different faults with different fixes.
    /// </summary>
    public DateTimeOffset AnnouncedAt { get; set; }
}
