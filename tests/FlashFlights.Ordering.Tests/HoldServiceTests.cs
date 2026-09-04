using FlashFlights.Ordering.Domain;
using FlashFlights.Ordering.Holds;
using Microsoft.EntityFrameworkCore;

namespace FlashFlights.Ordering.Tests;

/// <summary>
/// CreateHold against a real PostgreSQL: the happy path, the two failure shapes
/// the SPA has to tell apart (malformed 400 vs lost-the-race 409), and the
/// read-side expiry that lets a Seat be held again once its Hold's TTL passes —
/// without the sweep having run. The concurrency proof lives next door in
/// <see cref="CreateHoldConcurrencyTests"/>.
/// </summary>
[Collection(OrderingDatabaseCollection.Name)]
public class HoldServiceTests(OrderingDatabaseFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 9, 4, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Grants_a_hold_and_appends_held_movements_for_every_seat()
    {
        await using var db = fixture.NewDbContext();
        var flightId = Guid.NewGuid();
        var seatIds = await OrderingTestData.SeedFlightWithSeatsAsync(db, flightId, count: 2);
        var userId = Guid.NewGuid();

        var result = await OrderingTestData
            .HoldServiceFor(db, new TestClock(Now), TimeSpan.FromMinutes(2))
            .CreateHoldAsync(new CreateHoldRequest(flightId, seatIds, userId, PricePerSeat: 49.99m));

        var granted = Assert.IsType<CreateHoldResult.Granted>(result);
        Assert.Equal(userId, granted.Hold.UserId);
        Assert.Equal(Now.AddMinutes(2), granted.Hold.ExpiresAt);
        Assert.Equal(["1A", "1B"], granted.Hold.Seats.Select(seat => seat.SeatNumber).Order().ToArray());

        var heldSeatIds = await db.SeatMovements
            .Where(movement => movement.Type == SeatMovementType.Held && seatIds.Contains(movement.SeatId))
            .Select(movement => movement.SeatId)
            .ToListAsync();
        Assert.Equal(seatIds.Order(), heldSeatIds.Order());
    }

    [Fact]
    public async Task Holding_an_already_held_seat_conflicts_and_names_it()
    {
        await using var db = fixture.NewDbContext();
        var flightId = Guid.NewGuid();
        var seatIds = await OrderingTestData.SeedFlightWithSeatsAsync(db, flightId, count: 1);
        var service = OrderingTestData.HoldServiceFor(db, new TestClock(Now));

        await service.CreateHoldAsync(new CreateHoldRequest(flightId, seatIds, Guid.NewGuid(), 49.99m));
        var second = await service.CreateHoldAsync(new CreateHoldRequest(flightId, seatIds, Guid.NewGuid(), 49.99m));

        var conflict = Assert.IsType<CreateHoldResult.Conflict>(second);
        Assert.Equal("1A", Assert.Single(conflict.Seats).SeatNumber);
        Assert.Equal(nameof(SeatStatus.Held), conflict.Seats[0].Status);
    }

    [Fact]
    public async Task Holding_a_confirmed_seat_conflicts()
    {
        await using var db = fixture.NewDbContext();
        var flightId = Guid.NewGuid();
        var seatIds = await OrderingTestData.SeedFlightWithSeatsAsync(db, flightId, count: 1);
        await OrderingTestData.AppendMovementAsync(db, seatIds[0], flightId, SeatMovementType.Confirmed, Now);

        var result = await OrderingTestData
            .HoldServiceFor(db, new TestClock(Now))
            .CreateHoldAsync(new CreateHoldRequest(flightId, seatIds, Guid.NewGuid(), 49.99m));

        var conflict = Assert.IsType<CreateHoldResult.Conflict>(result);
        Assert.Equal(nameof(SeatStatus.Confirmed), Assert.Single(conflict.Seats).Status);
    }

    /// <summary>The point of "no partial success": a lost race for one Seat leaves the others untouched.</summary>
    [Fact]
    public async Task A_conflict_on_one_seat_holds_none_of_them()
    {
        await using var db = fixture.NewDbContext();
        var flightId = Guid.NewGuid();
        var seatIds = await OrderingTestData.SeedFlightWithSeatsAsync(db, flightId, count: 2);
        var service = OrderingTestData.HoldServiceFor(db, new TestClock(Now));

        // First buyer takes seat 0; second asks for both 0 and 1.
        await service.CreateHoldAsync(new CreateHoldRequest(flightId, [seatIds[0]], Guid.NewGuid(), 49.99m));
        var second = await service.CreateHoldAsync(new CreateHoldRequest(flightId, seatIds, Guid.NewGuid(), 49.99m));

        Assert.IsType<CreateHoldResult.Conflict>(second);

        // Seat 1 must still be free: it gained no Held movement from the failed request.
        var seatOneMovements = await db.SeatMovements.CountAsync(movement => movement.SeatId == seatIds[1]);
        Assert.Equal(0, seatOneMovements);
    }

    [Fact]
    public async Task Rejects_an_empty_seat_list_as_malformed()
    {
        await using var db = fixture.NewDbContext();

        var result = await OrderingTestData
            .HoldServiceFor(db, new TestClock(Now))
            .CreateHoldAsync(new CreateHoldRequest(Guid.NewGuid(), [], Guid.NewGuid(), 49.99m));

        var malformed = Assert.IsType<CreateHoldResult.Malformed>(result);
        Assert.Contains(HoldService.SeatIdsField, malformed.Errors.Keys);
    }

    [Fact]
    public async Task Rejects_the_same_seat_twice_as_malformed()
    {
        await using var db = fixture.NewDbContext();
        var flightId = Guid.NewGuid();
        var seatIds = await OrderingTestData.SeedFlightWithSeatsAsync(db, flightId, count: 1);

        var result = await OrderingTestData
            .HoldServiceFor(db, new TestClock(Now))
            .CreateHoldAsync(new CreateHoldRequest(flightId, [seatIds[0], seatIds[0]], Guid.NewGuid(), 49.99m));

        var malformed = Assert.IsType<CreateHoldResult.Malformed>(result);
        Assert.Contains(HoldService.SeatIdsField, malformed.Errors.Keys);
    }

    [Fact]
    public async Task Rejects_seats_that_are_not_of_the_flight_as_malformed()
    {
        await using var db = fixture.NewDbContext();
        var flightId = Guid.NewGuid();
        var otherFlightId = Guid.NewGuid();
        var seatOfOtherFlight = (await OrderingTestData.SeedFlightWithSeatsAsync(db, otherFlightId, count: 1))[0];
        var unknownSeat = Guid.NewGuid();

        var result = await OrderingTestData
            .HoldServiceFor(db, new TestClock(Now))
            .CreateHoldAsync(new CreateHoldRequest(flightId, [seatOfOtherFlight, unknownSeat], Guid.NewGuid(), 49.99m));

        var malformed = Assert.IsType<CreateHoldResult.Malformed>(result);
        Assert.Contains(HoldService.SeatIdsField, malformed.Errors.Keys);
    }

    [Fact]
    public async Task Rejects_a_negative_price_as_malformed()
    {
        await using var db = fixture.NewDbContext();
        var flightId = Guid.NewGuid();
        var seatIds = await OrderingTestData.SeedFlightWithSeatsAsync(db, flightId, count: 1);

        var result = await OrderingTestData
            .HoldServiceFor(db, new TestClock(Now))
            .CreateHoldAsync(new CreateHoldRequest(flightId, seatIds, Guid.NewGuid(), PricePerSeat: -1m));

        var malformed = Assert.IsType<CreateHoldResult.Malformed>(result);
        Assert.Contains(HoldService.PricePerSeatField, malformed.Errors.Keys);
    }

    /// <summary>
    /// The read side heals itself: once a Hold's TTL passes, its Seat computes
    /// back to Available and can be held again — no sweep required (ADR-0001).
    /// </summary>
    [Fact]
    public async Task A_hold_past_its_ttl_lets_the_seat_be_held_again_without_a_sweep()
    {
        await using var db = fixture.NewDbContext();
        var flightId = Guid.NewGuid();
        var seatIds = await OrderingTestData.SeedFlightWithSeatsAsync(db, flightId, count: 1);
        var clock = new TestClock(Now);
        var service = OrderingTestData.HoldServiceFor(db, clock, TimeSpan.FromMinutes(2));

        await service.CreateHoldAsync(new CreateHoldRequest(flightId, seatIds, Guid.NewGuid(), 49.99m));

        // Step past the TTL. No Released movement is posted — the read side must
        // still treat the Seat as free.
        clock.Advance(TimeSpan.FromMinutes(3));

        var afterExpiry = await service.CreateHoldAsync(
            new CreateHoldRequest(flightId, seatIds, Guid.NewGuid(), 49.99m));

        Assert.IsType<CreateHoldResult.Granted>(afterExpiry);
    }
}
