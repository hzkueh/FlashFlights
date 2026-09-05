using FlashFlights.Catalog.Browsing;

namespace FlashFlights.Catalog.Tests;

public class FlightCatalogServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_sale_whose_window_is_open_reads_as_live()
    {
        using var testDb = CatalogTestDb.Create();
        var flightId = await testDb.SeedFlightAsync(
            saleStartsAt: Now.AddHours(-1),
            saleEndsAt: Now.AddHours(1));

        var flight = await ServiceFor(testDb).GetFlightAsync(flightId);

        Assert.Equal(SaleState.Live, flight!.SaleState);
    }

    [Fact]
    public async Task A_sale_whose_window_has_not_opened_reads_as_upcoming()
    {
        using var testDb = CatalogTestDb.Create();
        var flightId = await testDb.SeedFlightAsync(
            saleStartsAt: Now.AddHours(1),
            saleEndsAt: Now.AddHours(3));

        var flight = await ServiceFor(testDb).GetFlightAsync(flightId);

        Assert.Equal(SaleState.Upcoming, flight!.SaleState);
    }

    [Fact]
    public async Task A_sale_whose_window_has_closed_reads_as_ended()
    {
        using var testDb = CatalogTestDb.Create();
        var flightId = await testDb.SeedFlightAsync(
            saleStartsAt: Now.AddHours(-3),
            saleEndsAt: Now.AddHours(-1));

        var flight = await ServiceFor(testDb).GetFlightAsync(flightId);

        Assert.Equal(SaleState.Ended, flight!.SaleState);
    }

    /// <summary>The boundaries are inclusive at open and exclusive at close, matching the seat map's clock.</summary>
    [Fact]
    public async Task At_the_instant_a_window_opens_the_sale_is_live()
    {
        using var testDb = CatalogTestDb.Create();
        var flightId = await testDb.SeedFlightAsync(saleStartsAt: Now, saleEndsAt: Now.AddHours(1));

        var flight = await ServiceFor(testDb).GetFlightAsync(flightId);

        Assert.Equal(SaleState.Live, flight!.SaleState);
    }

    [Fact]
    public async Task The_list_leads_with_live_sales_then_upcoming_then_ended()
    {
        using var testDb = CatalogTestDb.Create();
        var ended = await testDb.SeedFlightAsync("FF-ENDED", Now.AddHours(-5), Now.AddHours(-1));
        var upcoming = await testDb.SeedFlightAsync("FF-SOON", Now.AddHours(2), Now.AddHours(4));
        var live = await testDb.SeedFlightAsync("FF-LIVE", Now.AddHours(-1), Now.AddHours(1));

        var flights = await ServiceFor(testDb).ListFlightsAsync();

        Assert.Equal([live, upcoming, ended], flights.Select(flight => flight.Id).ToArray());
    }

    [Fact]
    public async Task Counts_are_carried_when_the_projection_row_exists()
    {
        using var testDb = CatalogTestDb.Create();
        var flightId = await testDb.SeedFlightAsync(totalSeats: 12);

        var flight = await ServiceFor(testDb).GetFlightAsync(flightId);

        Assert.Equal(new SeatCountsView(12, 12, 0, 0), flight!.SeatCounts);
    }

    /// <summary>
    /// No projection row yet means the counts are unknown, not zero — the SPA
    /// renders "—" rather than "sold out", so this is null, deliberately.
    /// </summary>
    [Fact]
    public async Task Counts_are_null_when_no_projection_row_exists()
    {
        using var testDb = CatalogTestDb.Create();
        var flightId = await testDb.SeedFlightAsync(totalSeats: null);

        var flight = await ServiceFor(testDb).GetFlightAsync(flightId);

        Assert.Null(flight!.SeatCounts);
    }

    /// <summary>
    /// ReferenceFare is display-only and optional (CONTEXT.md): when a Flight
    /// carries one it rides the view through to the SPA, which derives the saving.
    /// </summary>
    [Fact]
    public async Task A_reference_fare_is_carried_when_the_flight_has_one()
    {
        using var testDb = CatalogTestDb.Create();
        var flightId = await testDb.SeedFlightAsync(referenceFare: 79.99m);

        var flight = await ServiceFor(testDb).GetFlightAsync(flightId);

        Assert.Equal(79.99m, flight!.ReferenceFare);
    }

    /// <summary>Most flights have none — the exception, not the rule — and then no saving is shown.</summary>
    [Fact]
    public async Task A_reference_fare_is_null_when_the_flight_has_none()
    {
        using var testDb = CatalogTestDb.Create();
        var flightId = await testDb.SeedFlightAsync(referenceFare: null);

        var flight = await ServiceFor(testDb).GetFlightAsync(flightId);

        Assert.Null(flight!.ReferenceFare);
    }

    [Fact]
    public async Task An_unknown_flight_id_returns_null()
    {
        using var testDb = CatalogTestDb.Create();

        Assert.Null(await ServiceFor(testDb).GetFlightAsync(Guid.NewGuid()));
    }

    private static FlightCatalogService ServiceFor(CatalogTestDb testDb) =>
        new(testDb.NewContext(), new FixedClock(Now));

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
