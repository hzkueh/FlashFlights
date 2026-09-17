namespace FlashFlights.Ordering.Domain;

/// <summary>
/// Ordering's view of one Flight's sale window, assembled from the announcements
/// it has heard.
///
/// <para>
/// Deliberately <em>not</em> Catalog's <c>SaleState</c> vocabulary
/// (Upcoming/Live/Ended), which the SPA's <c>saleStateAt</c> shares: Catalog
/// reads a window it owns and can say a sale is Upcoming, while Ordering can only
/// say it has heard nothing — which is an Upcoming sale nearly always, and an
/// announcement still in flight occasionally. Borrowing the word "Upcoming" would
/// claim a certainty this service does not have. Only the <em>boundaries</em> are
/// shared, and <see cref="SaleWindowRules"/> is where they are kept in step.
/// </para>
/// </summary>
public enum SaleWindowState
{
    /// <summary>
    /// No announcement for this Flight has reached Ordering. Almost always an
    /// Upcoming sale — one that has not opened has nothing to announce — and
    /// briefly a sale whose announcement is still in flight (ADR-0003).
    /// </summary>
    NotStarted,

    /// <summary>The flash price is on offer: the sale opened and has not closed.</summary>
    Open,

    /// <summary>The window has closed. Nothing about this Flight is on offer any more.</summary>
    Ended,
}

/// <summary>
/// The one place Ordering decides whether a Flight's flash price is on offer —
/// ADR-0003's rule, whole, as a function of what this service has been told and
/// the clock. <see cref="SeatStatusRules"/>'s counterpart for the Flight: the
/// Seat's takeability is one function, and so is the Flight's.
///
/// <para>
/// The end boundary is Catalog's: <c>FlightCatalogService.StateOf</c> and the
/// SPA's <c>saleStateAt</c> both treat the close as exclusive, so at the instant
/// a sale closes the page reads Ended and a Hold is refused. A change to one of
/// the three belongs in the others. Their <em>inclusive start</em> has no
/// counterpart here: this service never learns <c>SaleStartsAt</c> and carries
/// the opening only as whether an announcement has arrived (ADR-0003).
/// </para>
/// </summary>
public static class SaleWindowRules
{
    /// <summary>
    /// Whether <paramref name="announcement"/>'s Flight is on offer as of
    /// <paramref name="now"/>. A null announcement is a Flight Ordering has heard
    /// nothing about, which is <see cref="SaleWindowState.NotStarted"/> and never
    /// a grant: an Upcoming sale has no announcement by definition, so treating
    /// the unknown as permitted would leave every sale that has not opened yet
    /// holdable (ADR-0003).
    /// </summary>
    public static SaleWindowState StateOf(SaleAnnouncement? announcement, DateTimeOffset now) => announcement switch
    {
        null => SaleWindowState.NotStarted,
        _ => now < announcement.SaleEndsAt ? SaleWindowState.Open : SaleWindowState.Ended,
    };
}
