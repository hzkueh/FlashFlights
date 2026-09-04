using FlashFlights.Ordering.Domain;
using FlashFlights.Ordering.Holds;
using Microsoft.EntityFrameworkCore;

namespace FlashFlights.Ordering.Tests;

/// <summary>
/// The expiry sweep against a real PostgreSQL: it posts the compensating
/// Released movements that a silently expired Hold needs for Catalog to learn of
/// it (ADR-0001), it agrees with the read side and confirm on the exact TTL
/// boundary, and it is safe to run against live data — it never releases a Seat a
/// confirm won or a newer Hold has taken.
///
/// The sweep is global, and these tests share the container, so a run also
/// releases other tests' leftover expired Holds. That makes the run's aggregate
/// counts (<see cref="ExpireHoldsResult.SeatsReleased"/>) shared state — the
/// assertions here are keyed to each test's own Seats instead, which is the only
/// state a test owns exclusively.
/// </summary>
[Collection(OrderingDatabaseCollection.Name)]
public class ExpireHoldsTests(OrderingDatabaseFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 9, 4, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(2);

    /// <summary>hold → expire → the Seat is Available again, and a durable Released movement records why.</summary>
    [Fact]
    public async Task Sweeping_an_expired_hold_releases_its_seats_and_records_the_movement()
    {
        await using var db = fixture.NewDbContext();
        var flightId = Guid.NewGuid();
        var seatIds = await OrderingTestData.SeedFlightWithSeatsAsync(db, flightId, count: 2);
        var clock = new TestClock(Now);
        var service = OrderingTestData.HoldServiceFor(db, clock, Ttl);

        await service.CreateHoldAsync(new CreateHoldRequest(flightId, seatIds, Guid.NewGuid(), 49.99m));

        clock.Advance(Ttl);
        var result = await service.ExpireHoldsAsync();

        // The run did work — at least this Hold's two Seats (the count is a shared
        // aggregate, so only a lower bound is safe to assert).
        Assert.True(result.SeatsReleased >= 2, $"expected at least 2 seats released, got {result.SeatsReleased}");

        // A durable Released movement per Seat — the audit record Catalog reads.
        var releasedSeatIds = await db.SeatMovements
            .Where(movement => movement.Type == SeatMovementType.Released && seatIds.Contains(movement.SeatId))
            .Select(movement => movement.SeatId)
            .ToListAsync();
        Assert.Equal(seatIds.Order(), releasedSeatIds.Order());

        // Available again in the strongest sense: a fresh Hold over the Seats wins.
        var reHeld = await service.CreateHoldAsync(new CreateHoldRequest(flightId, seatIds, Guid.NewGuid(), 49.99m));
        Assert.IsType<CreateHoldResult.Granted>(reHeld);
    }

    /// <summary>
    /// The boundary is the one the read side and confirm use: a Hold exactly at
    /// its TTL is expired, so the sweep releases it — never live to one path and
    /// expired to another.
    /// </summary>
    [Fact]
    public async Task Sweeps_a_hold_exactly_at_its_ttl()
    {
        await using var db = fixture.NewDbContext();
        var flightId = Guid.NewGuid();
        var seatIds = await OrderingTestData.SeedFlightWithSeatsAsync(db, flightId, count: 1);
        var clock = new TestClock(Now);
        var service = OrderingTestData.HoldServiceFor(db, clock, Ttl);

        await service.CreateHoldAsync(new CreateHoldRequest(flightId, seatIds, Guid.NewGuid(), 49.99m));

        clock.Advance(Ttl); // now == ExpiresAt; HasExpired is inclusive.
        await service.ExpireHoldsAsync();

        Assert.Equal(1, await ReleasedCountAsync(db, seatIds[0]));
    }

    /// <summary>A Hold a moment short of its TTL is still live — the sweep must leave it and its Seat alone.</summary>
    [Fact]
    public async Task Leaves_a_hold_that_has_not_yet_reached_its_ttl()
    {
        await using var db = fixture.NewDbContext();
        var flightId = Guid.NewGuid();
        var seatIds = await OrderingTestData.SeedFlightWithSeatsAsync(db, flightId, count: 1);
        var clock = new TestClock(Now);
        var service = OrderingTestData.HoldServiceFor(db, clock, Ttl);

        await service.CreateHoldAsync(new CreateHoldRequest(flightId, seatIds, Guid.NewGuid(), 49.99m));

        clock.Advance(Ttl - TimeSpan.FromSeconds(1));
        await service.ExpireHoldsAsync();

        Assert.Equal(0, await ReleasedCountAsync(db, seatIds[0]));
    }

    /// <summary>Running the sweep twice must not post a second Released — it is idempotent.</summary>
    [Fact]
    public async Task A_second_sweep_does_not_release_the_same_hold_again()
    {
        await using var db = fixture.NewDbContext();
        var flightId = Guid.NewGuid();
        var seatIds = await OrderingTestData.SeedFlightWithSeatsAsync(db, flightId, count: 1);
        var clock = new TestClock(Now);
        var service = OrderingTestData.HoldServiceFor(db, clock, Ttl);

        await service.CreateHoldAsync(new CreateHoldRequest(flightId, seatIds, Guid.NewGuid(), 49.99m));

        clock.Advance(Ttl);
        await service.ExpireHoldsAsync();
        Assert.Equal(1, await ReleasedCountAsync(db, seatIds[0]));

        await service.ExpireHoldsAsync();
        Assert.Equal(1, await ReleasedCountAsync(db, seatIds[0]));
    }

    /// <summary>A confirmed Hold is a Booking, not an expiry — the sweep must never release its Seats.</summary>
    [Fact]
    public async Task Does_not_release_a_confirmed_hold()
    {
        await using var db = fixture.NewDbContext();
        var flightId = Guid.NewGuid();
        var seatIds = await OrderingTestData.SeedFlightWithSeatsAsync(db, flightId, count: 1);
        var userId = Guid.NewGuid();
        var clock = new TestClock(Now);
        var service = OrderingTestData.HoldServiceFor(db, clock, Ttl);

        var granted = Assert.IsType<CreateHoldResult.Granted>(
            await service.CreateHoldAsync(new CreateHoldRequest(flightId, seatIds, userId, 49.99m)));
        await service.ConfirmHoldAsync(granted.Hold.HoldId, userId);

        // Past the TTL, but confirmed holds are gone for good, not expired.
        clock.Advance(Ttl + TimeSpan.FromMinutes(5));
        await service.ExpireHoldsAsync();

        Assert.Equal(0, await ReleasedCountAsync(db, seatIds[0]));
    }

    /// <summary>
    /// If read-side expiry let a newer Hold take a Seat before the sweep ran, the
    /// stale Hold's sweep must not release it — that Released would wrongly free
    /// the newer Hold's Seat. The sweep releases only Seats whose latest movement
    /// is still the expiring Hold's own Held.
    /// </summary>
    [Fact]
    public async Task Does_not_release_a_seat_a_newer_hold_has_taken()
    {
        await using var db = fixture.NewDbContext();
        var flightId = Guid.NewGuid();
        var seatIds = await OrderingTestData.SeedFlightWithSeatsAsync(db, flightId, count: 1);
        var clock = new TestClock(Now);
        var service = OrderingTestData.HoldServiceFor(db, clock, Ttl);

        // First hold, then step past its TTL without sweeping.
        await service.CreateHoldAsync(new CreateHoldRequest(flightId, seatIds, Guid.NewGuid(), 49.99m));
        clock.Advance(Ttl);

        // A second buyer wins the Seat via read-side expiry — a new live Held.
        var newer = Assert.IsType<CreateHoldResult.Granted>(
            await service.CreateHoldAsync(new CreateHoldRequest(flightId, seatIds, Guid.NewGuid(), 49.99m)));

        await service.ExpireHoldsAsync();

        // The stale hold released nothing: its Seat has moved on to the newer Hold.
        Assert.Equal(0, await ReleasedCountAsync(db, seatIds[0]));

        // And the newer Hold is intact — still holding, not swept out from under it.
        var confirmed = await service.ConfirmHoldAsync(newer.Hold.HoldId, newer.Hold.UserId);
        Assert.IsType<ConfirmHoldResult.Confirmed>(confirmed);
    }

    private static Task<int> ReleasedCountAsync(Persistence.OrderingDbContext db, Guid seatId) =>
        db.SeatMovements.CountAsync(
            movement => movement.SeatId == seatId && movement.Type == SeatMovementType.Released);
}
