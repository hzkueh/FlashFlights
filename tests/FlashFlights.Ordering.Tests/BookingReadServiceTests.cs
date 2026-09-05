using FlashFlights.Ordering.Bookings;
using FlashFlights.Ordering.Holds;

namespace FlashFlights.Ordering.Tests;

/// <summary>
/// Booking history against a real PostgreSQL: a buyer's confirmed purchases,
/// newest first, each carrying its Flight id and the Seats it bought — and
/// scoped to the one buyer, never leaking another's Bookings. The Seats are read
/// back from the Hold's Confirmed movements, the same ledger the grant path
/// wrote them to, so the labels a buyer sees in history match the ones they
/// confirmed.
/// </summary>
[Collection(OrderingDatabaseCollection.Name)]
public class BookingReadServiceTests(OrderingDatabaseFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_buyer_with_no_bookings_gets_an_empty_list()
    {
        await using var db = fixture.NewDbContext();

        var bookings = await new BookingReadService(db).ListBookingsForUserAsync(Guid.NewGuid());

        Assert.Empty(bookings);
    }

    [Fact]
    public async Task Lists_a_booking_with_its_flight_seats_and_price_paid()
    {
        await using var db = fixture.NewDbContext();
        var flightId = Guid.NewGuid();
        var seatIds = await OrderingTestData.SeedFlightWithSeatsAsync(db, flightId, count: 2);
        var userId = Guid.NewGuid();
        var service = OrderingTestData.HoldServiceFor(db, new TestClock(Now));

        var granted = Assert.IsType<CreateHoldResult.Granted>(
            await service.CreateHoldAsync(new CreateHoldRequest(flightId, seatIds, userId, PricePerSeat: 49.99m)));
        await service.ConfirmHoldAsync(granted.Hold.HoldId, userId);

        var bookings = await new BookingReadService(db).ListBookingsForUserAsync(userId);

        var booking = Assert.Single(bookings);
        Assert.Equal(granted.Hold.HoldId, booking.HoldId);
        Assert.Equal(flightId, booking.FlightId);
        Assert.Equal(userId, booking.UserId);
        // Two Seats at the frozen FlashPrice — the same total the confirm charged.
        Assert.Equal(99.98m, booking.PricePaid);
        Assert.Equal(["1A", "1B"], booking.Seats.Select(seat => seat.SeatNumber).ToArray());
    }

    /// <summary>Newest confirmed first, so a buyer scans their latest purchase at the top.</summary>
    [Fact]
    public async Task Lists_a_buyers_bookings_newest_first()
    {
        await using var db = fixture.NewDbContext();
        var flightId = Guid.NewGuid();
        var seatIds = await OrderingTestData.SeedFlightWithSeatsAsync(db, flightId, count: 2);
        var userId = Guid.NewGuid();
        var clock = new TestClock(Now);
        var service = OrderingTestData.HoldServiceFor(db, clock);

        var first = Assert.IsType<CreateHoldResult.Granted>(
            await service.CreateHoldAsync(new CreateHoldRequest(flightId, [seatIds[0]], userId, 49.99m)));
        await service.ConfirmHoldAsync(first.Hold.HoldId, userId);

        clock.Advance(TimeSpan.FromMinutes(1));

        var second = Assert.IsType<CreateHoldResult.Granted>(
            await service.CreateHoldAsync(new CreateHoldRequest(flightId, [seatIds[1]], userId, 49.99m)));
        await service.ConfirmHoldAsync(second.Hold.HoldId, userId);

        var bookings = await new BookingReadService(db).ListBookingsForUserAsync(userId);

        Assert.Equal(
            [second.Hold.HoldId, first.Hold.HoldId],
            bookings.Select(booking => booking.HoldId).ToArray());
    }

    /// <summary>History is scoped to the token's buyer — another buyer's Bookings never appear.</summary>
    [Fact]
    public async Task Does_not_list_another_buyers_bookings()
    {
        await using var db = fixture.NewDbContext();
        var flightId = Guid.NewGuid();
        var seatIds = await OrderingTestData.SeedFlightWithSeatsAsync(db, flightId, count: 2);
        var mine = Guid.NewGuid();
        var theirs = Guid.NewGuid();
        var service = OrderingTestData.HoldServiceFor(db, new TestClock(Now));

        var myHold = Assert.IsType<CreateHoldResult.Granted>(
            await service.CreateHoldAsync(new CreateHoldRequest(flightId, [seatIds[0]], mine, 49.99m)));
        await service.ConfirmHoldAsync(myHold.Hold.HoldId, mine);

        var theirHold = Assert.IsType<CreateHoldResult.Granted>(
            await service.CreateHoldAsync(new CreateHoldRequest(flightId, [seatIds[1]], theirs, 49.99m)));
        await service.ConfirmHoldAsync(theirHold.Hold.HoldId, theirs);

        var bookings = await new BookingReadService(db).ListBookingsForUserAsync(mine);

        Assert.Equal(myHold.Hold.HoldId, Assert.Single(bookings).HoldId);
    }
}
