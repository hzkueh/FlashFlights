using FlashFlights.Catalog.Browsing;
using FlashFlights.Catalog.Seeding;
using FlashFlights.DemoData;
using Microsoft.EntityFrameworkCore;

namespace FlashFlights.Catalog.Tests;

/// <summary>
/// Catalog's half of ticket 11: a fresh clone's list page comes up populated,
/// and a restart leaves it exactly as it was.
/// </summary>
public class CatalogDemoSeederTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 17, 9, 0, 0, TimeSpan.Zero);

    private static DemoWorld World() => DemoWorld.Create(Now, new DemoSeedOptions());

    [Fact]
    public async Task Seeds_every_flight_in_the_demo_world()
    {
        using var testDb = CatalogTestDb.Create();

        Assert.True(await SeedAsync(testDb));

        await using var db = testDb.NewContext();
        var seeded = await db.Flights.ToListAsync();

        Assert.Equal(
            World().Flights.Select(flight => flight.FlightNumber).Order(),
            seeded.Select(flight => flight.FlightNumber).Order());
    }

    /// <summary>
    /// The ticket's headline: all three sale states are on the list page without
    /// anyone clicking them into existence.
    /// </summary>
    [Fact]
    public async Task The_list_page_shows_upcoming_live_and_ended_sales_on_a_fresh_store()
    {
        using var testDb = CatalogTestDb.Create();
        await SeedAsync(testDb);

        await using var db = testDb.NewContext();
        var listed = await new FlightCatalogService(db, new FixedClock(Now)).ListFlightsAsync();

        Assert.Contains(listed, flight => flight.SaleState == SaleState.Upcoming);
        Assert.Contains(listed, flight => flight.SaleState == SaleState.Live);
        Assert.Contains(listed, flight => flight.SaleState == SaleState.Ended);
    }

    [Fact]
    public async Task Carries_the_reference_fare_through_for_the_flights_that_have_one()
    {
        using var testDb = CatalogTestDb.Create();
        await SeedAsync(testDb);

        await using var db = testDb.NewContext();
        var seeded = await db.Flights.ToListAsync();

        Assert.Contains(seeded, flight => flight.ReferenceFare > flight.FlashPrice);
        Assert.Contains(seeded, flight => flight.ReferenceFare is null);
    }

    /// <summary>
    /// The counts Ordering's seeded ledger will agree with. They are advisory
    /// (CONTEXT.md) and allowed to trail, but at the moment of seeding there is
    /// nothing for them to trail — so a disagreement here is a seeding bug, not
    /// the eventual consistency the design allows for.
    /// </summary>
    [Fact]
    public async Task Seeds_counts_that_match_the_seats_ordering_will_hold_and_confirm()
    {
        using var testDb = CatalogTestDb.Create();
        await SeedAsync(testDb);

        await using var db = testDb.NewContext();
        var counts = await db.FlightSeatCounts.ToDictionaryAsync(row => row.FlightId);

        Assert.All(World().Flights, flight =>
        {
            var seeded = counts[flight.Id];

            Assert.Equal(flight.TotalSeats, seeded.TotalSeats);
            Assert.Equal(flight.AvailableSeats, seeded.AvailableSeats);
            Assert.Equal(flight.HeldSeats, seeded.HeldSeats);
            Assert.Equal(flight.ConfirmedSeats, seeded.ConfirmedSeats);
        });
    }

    /// <summary>
    /// The high-water mark has to sit behind the first movement a buyer causes,
    /// or the projector drops it as a straggler and the counts stop moving.
    /// </summary>
    [Fact]
    public async Task Leaves_the_counts_high_water_mark_behind_the_present()
    {
        using var testDb = CatalogTestDb.Create();
        await SeedAsync(testDb);

        await using var db = testDb.NewContext();

        Assert.All(
            await db.FlightSeatCounts.ToListAsync(),
            counts => Assert.True(counts.LastMovementAt <= Now));
    }

    /// <summary>
    /// A seeded Flight whose sale has opened must still be announced: Ordering
    /// refuses a Hold on a Flight it has heard no announcement for (ADR-0003), so
    /// marking these settled would seed a catalog nobody can buy from.
    /// </summary>
    [Fact]
    public async Task Leaves_every_seeded_crossing_for_the_announcer_to_settle()
    {
        using var testDb = CatalogTestDb.Create();
        await SeedAsync(testDb);

        await using var db = testDb.NewContext();

        Assert.All(await db.Flights.ToListAsync(), flight => Assert.Null(flight.SaleStartHandledAt));
    }

    [Fact]
    public async Task A_second_run_over_a_seeded_catalog_changes_nothing()
    {
        using var testDb = CatalogTestDb.Create();
        await SeedAsync(testDb);

        var before = await SnapshotAsync(testDb);

        Assert.False(await SeedAsync(testDb));
        Assert.Equal(before, await SnapshotAsync(testDb));
    }

    /// <summary>
    /// Restarting is not the only way a store comes back non-empty: it may hold
    /// flights this seeder did not write. Either way the answer is the same —
    /// the seed fills an empty catalog and never rewrites a full one.
    /// </summary>
    [Fact]
    public async Task Leaves_a_catalog_that_already_has_flights_alone()
    {
        using var testDb = CatalogTestDb.Create();
        await testDb.SeedFlightAsync("FF900");

        Assert.False(await SeedAsync(testDb));

        await using var db = testDb.NewContext();
        Assert.Equal("FF900", (await db.Flights.SingleAsync()).FlightNumber);
    }

    private static async Task<bool> SeedAsync(CatalogTestDb testDb)
    {
        await using var db = testDb.NewContext();

        return await new CatalogDemoSeeder(db).SeedAsync(World());
    }

    /// <summary>Everything a reseed could have quietly rewritten.</summary>
    private static async Task<string> SnapshotAsync(CatalogTestDb testDb)
    {
        await using var db = testDb.NewContext();

        var flights = await db.Flights.ToListAsync();
        var counts = await db.FlightSeatCounts.ToListAsync();

        return string.Join(
            "\n",
            flights
                .OrderBy(flight => flight.FlightNumber, StringComparer.Ordinal)
                .Select(flight => string.Join(
                    "|",
                    flight.Id,
                    flight.FlightNumber,
                    flight.Origin,
                    flight.Destination,
                    flight.DepartureAt.ToString("O"),
                    flight.FlashPrice,
                    flight.ReferenceFare,
                    flight.SaleStartsAt.ToString("O"),
                    flight.SaleEndsAt.ToString("O")))
                .Concat(counts
                    .OrderBy(row => row.FlightId)
                    .Select(row => string.Join(
                        "|",
                        row.FlightId,
                        row.TotalSeats,
                        row.AvailableSeats,
                        row.HeldSeats,
                        row.ConfirmedSeats,
                        row.LastMovementAt.ToString("O")))));
    }
}
