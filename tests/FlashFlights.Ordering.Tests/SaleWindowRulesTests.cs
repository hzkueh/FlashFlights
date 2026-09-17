using FlashFlights.Ordering.Domain;

namespace FlashFlights.Ordering.Tests;

/// <summary>
/// The whole of ADR-0003's rule as a pure function: whether Ordering may grant a
/// Hold on a Flight, given the announcement it has heard (if any) and the clock.
///
/// <para>
/// The boundaries are Catalog's, deliberately — <c>FlightCatalogService.StateOf</c>
/// and the SPA's <c>saleStateAt</c> both treat the window as inclusive at the
/// start and exclusive at the end. A page that shows Ended and a service that
/// still grants Holds is the disagreement this ticket exists to remove, so the
/// tie at the closing instant is pinned here.
/// </para>
/// </summary>
public class SaleWindowRulesTests
{
    private static readonly DateTimeOffset SaleEndsAt = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_window_that_has_not_closed_is_open()
    {
        var state = SaleWindowRules.StateOf(Announced(SaleEndsAt), SaleEndsAt.AddSeconds(-1));

        Assert.Equal(SaleWindowState.Open, state);
    }

    /// <summary>
    /// Exclusive at the end, matching Catalog: at the instant a window closes the
    /// sale reads Ended on the page, so a Hold posted at that instant must be
    /// refused rather than squeak through.
    /// </summary>
    [Fact]
    public void At_the_closing_instant_the_window_is_ended()
    {
        var state = SaleWindowRules.StateOf(Announced(SaleEndsAt), SaleEndsAt);

        Assert.Equal(SaleWindowState.Ended, state);
    }

    [Fact]
    public void After_the_closing_instant_the_window_is_ended()
    {
        var state = SaleWindowRules.StateOf(Announced(SaleEndsAt), SaleEndsAt.AddMinutes(5));

        Assert.Equal(SaleWindowState.Ended, state);
    }

    /// <summary>
    /// ADR-0003's decision, and the half of the bug the SPA's gate could never
    /// have closed: an Upcoming sale has no announcement <em>by definition</em>,
    /// so "no announcement heard" has to mean "no Hold" or every sale that has
    /// not opened yet is holdable.
    /// </summary>
    [Fact]
    public void A_flight_no_announcement_has_been_heard_for_is_not_open()
    {
        var state = SaleWindowRules.StateOf(announcement: null, SaleEndsAt.AddSeconds(-1));

        Assert.Equal(SaleWindowState.NotStarted, state);
    }

    private static SaleAnnouncement Announced(DateTimeOffset saleEndsAt) =>
        new() { FlightId = Guid.NewGuid(), SaleEndsAt = saleEndsAt, AnnouncedAt = saleEndsAt.AddHours(-1) };
}
