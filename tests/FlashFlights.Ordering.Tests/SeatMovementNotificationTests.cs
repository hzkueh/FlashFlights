using FlashFlights.Ordering.Domain;
using FlashFlights.Ordering.Holds;

namespace FlashFlights.Ordering.Tests;

/// <summary>
/// The events Ordering owes Catalog: a granted Hold announces SeatsHeld, a
/// confirm announces SeatsConfirmed, and the expiry sweep announces SeatsReleased
/// for the Seats it actually freed. Asserted through the notifier seam, so no
/// broker is involved — the guarantee is that the right announcement is made for
/// the right Seats, not how it reaches the bus.
/// </summary>
[Collection(OrderingDatabaseCollection.Name)]
public class SeatMovementNotificationTests(OrderingDatabaseFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Granting_a_hold_announces_the_held_seats()
    {
        await using var db = fixture.NewDbContext();
        var flightId = Guid.NewGuid();
        var seatIds = await OrderingTestData.SeedFlightWithSeatsAsync(db, flightId, count: 3);
        var notifier = new RecordingSeatMovementNotifier();

        await OrderingTestData
            .HoldServiceFor(db, new TestClock(Now), notifier: notifier)
            .CreateHoldAsync(new CreateHoldRequest(flightId, seatIds, Guid.NewGuid(), 49.99m));

        var announced = Assert.Single(notifier.Held);
        Assert.Equal(flightId, announced.FlightId);
        Assert.Equal(seatIds.Order(), announced.SeatIds.Order());
        Assert.Empty(notifier.Released);
        Assert.Empty(notifier.Confirmed);
    }

    [Fact]
    public async Task Confirming_a_hold_announces_the_confirmed_seats()
    {
        await using var db = fixture.NewDbContext();
        var flightId = Guid.NewGuid();
        var seatIds = await OrderingTestData.SeedFlightWithSeatsAsync(db, flightId, count: 2);
        var notifier = new RecordingSeatMovementNotifier();
        var service = OrderingTestData.HoldServiceFor(db, new TestClock(Now), notifier: notifier);

        var granted = Assert.IsType<CreateHoldResult.Granted>(
            await service.CreateHoldAsync(new CreateHoldRequest(flightId, seatIds, Guid.NewGuid(), 49.99m)));
        await service.ConfirmHoldAsync(granted.Hold.HoldId, granted.Hold.UserId);

        var announced = Assert.Single(notifier.Confirmed);
        Assert.Equal(flightId, announced.FlightId);
        Assert.Equal(seatIds.Order(), announced.SeatIds.Order());
    }

    [Fact]
    public async Task The_sweep_announces_only_the_seats_it_actually_released()
    {
        await using var db = fixture.NewDbContext();
        var flightId = Guid.NewGuid();
        var seatIds = await OrderingTestData.SeedFlightWithSeatsAsync(db, flightId, count: 2);
        var notifier = new RecordingSeatMovementNotifier();
        var clock = new TestClock(Now);
        var service = OrderingTestData.HoldServiceFor(db, clock, TimeSpan.FromMinutes(2), notifier);

        await service.CreateHoldAsync(new CreateHoldRequest(flightId, seatIds, Guid.NewGuid(), 49.99m));
        clock.Advance(TimeSpan.FromMinutes(3));
        await service.ExpireHoldsAsync();

        // The sweep is global over the shared test database, so it may also
        // release other tests' expired Holds; scope the assertion to this flight.
        var announced = Assert.Single(notifier.Released, release => release.FlightId == flightId);
        Assert.Equal(seatIds.Order(), announced.SeatIds.Order());
    }

    /// <summary>
    /// A sweep pass that frees nothing — no expired Holds — says nothing, so
    /// Catalog is never handed an empty release to fold in.
    /// </summary>
    [Fact]
    public async Task A_sweep_that_releases_nothing_announces_nothing()
    {
        await using var db = fixture.NewDbContext();
        var flightId = Guid.NewGuid();
        var seatIds = await OrderingTestData.SeedFlightWithSeatsAsync(db, flightId, count: 1);
        var notifier = new RecordingSeatMovementNotifier();
        var service = OrderingTestData.HoldServiceFor(db, new TestClock(Now), TimeSpan.FromMinutes(2), notifier);

        // A live Hold, nowhere near its TTL — the sweep finds nothing to release
        // for this flight (it may still release other tests' expired Holds).
        await service.CreateHoldAsync(new CreateHoldRequest(flightId, seatIds, Guid.NewGuid(), 49.99m));
        await service.ExpireHoldsAsync();

        Assert.DoesNotContain(notifier.Released, release => release.FlightId == flightId);
    }
}
