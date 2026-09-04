using FlashFlights.Ordering.Domain;
using FlashFlights.Ordering.Holds;
using FlashFlights.Ordering.SeatMaps;

namespace FlashFlights.Ordering.Tests;

/// <summary>
/// The live seat map browsing reads: every Seat with the status derived from its
/// latest movement, the same rule the locked grant path uses. Includes the case
/// that matters most for browsing — a Held past its TTL reads back Available here
/// without the sweep having run.
/// </summary>
[Collection(OrderingDatabaseCollection.Name)]
public class SeatMapServiceTests(OrderingDatabaseFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_flight_with_no_movements_is_all_available()
    {
        await using var db = fixture.NewDbContext();
        var flightId = Guid.NewGuid();
        await OrderingTestData.SeedFlightWithSeatsAsync(db, flightId, count: 3);

        var map = await new SeatMapService(db, new TestClock(Now)).GetSeatMapAsync(flightId);

        Assert.Equal(3, map.Seats.Count);
        Assert.All(map.Seats, seat => Assert.Equal(SeatStatus.Available, seat.Status));
    }

    [Fact]
    public async Task A_held_seat_reads_held_and_a_confirmed_seat_reads_confirmed()
    {
        await using var db = fixture.NewDbContext();
        var flightId = Guid.NewGuid();
        var seatIds = await OrderingTestData.SeedFlightWithSeatsAsync(db, flightId, count: 3);
        var service = OrderingTestData.HoldServiceFor(db, new TestClock(Now));

        // Seat 0 held-and-confirmed, seat 1 held only, seat 2 left available.
        var confirmHold = Assert.IsType<CreateHoldResult.Granted>(
            await service.CreateHoldAsync(new CreateHoldRequest(flightId, [seatIds[0]], Guid.NewGuid(), 49.99m)));
        await service.ConfirmHoldAsync(confirmHold.Hold.HoldId, confirmHold.Hold.UserId);
        await service.CreateHoldAsync(new CreateHoldRequest(flightId, [seatIds[1]], Guid.NewGuid(), 49.99m));

        var map = await new SeatMapService(db, new TestClock(Now)).GetSeatMapAsync(flightId);
        var byId = map.Seats.ToDictionary(seat => seat.SeatId, seat => seat.Status);

        Assert.Equal(SeatStatus.Confirmed, byId[seatIds[0]]);
        Assert.Equal(SeatStatus.Held, byId[seatIds[1]]);
        Assert.Equal(SeatStatus.Available, byId[seatIds[2]]);
    }

    /// <summary>
    /// The self-healing case: a Held whose TTL has passed reads Available to the
    /// map the instant it lapses, before any Released movement is posted — the map
    /// leads the counts (ADR-0001).
    /// </summary>
    [Fact]
    public async Task A_held_seat_past_its_ttl_reads_available_before_the_sweep_runs()
    {
        await using var db = fixture.NewDbContext();
        var flightId = Guid.NewGuid();
        var seatIds = await OrderingTestData.SeedFlightWithSeatsAsync(db, flightId, count: 1);
        var clock = new TestClock(Now);
        await OrderingTestData
            .HoldServiceFor(db, clock, TimeSpan.FromMinutes(2))
            .CreateHoldAsync(new CreateHoldRequest(flightId, seatIds, Guid.NewGuid(), 49.99m));

        // Past the TTL, but no sweep has run — the ledger still holds only the Held.
        clock.Advance(TimeSpan.FromMinutes(3));
        var map = await new SeatMapService(db, clock).GetSeatMapAsync(flightId);

        Assert.Equal(SeatStatus.Available, Assert.Single(map.Seats).Status);
    }

    [Fact]
    public async Task Seats_come_back_in_grid_order()
    {
        await using var db = fixture.NewDbContext();
        var flightId = Guid.NewGuid();
        await OrderingTestData.SeedFlightWithSeatsAsync(db, flightId, count: 3);

        var map = await new SeatMapService(db, new TestClock(Now)).GetSeatMapAsync(flightId);

        Assert.Equal(["1A", "1B", "1C"], map.Seats.Select(seat => seat.SeatNumber).ToArray());
    }

    [Fact]
    public async Task An_unknown_flight_has_an_empty_map()
    {
        await using var db = fixture.NewDbContext();

        var map = await new SeatMapService(db, new TestClock(Now)).GetSeatMapAsync(Guid.NewGuid());

        Assert.Empty(map.Seats);
    }
}
