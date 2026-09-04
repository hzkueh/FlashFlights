using FlashFlights.Ordering.Domain;
using FlashFlights.Ordering.Holds;
using Microsoft.EntityFrameworkCore;

namespace FlashFlights.Ordering.Tests;

/// <summary>
/// The reason this whole project exists: two buyers can never both win the same
/// Seat. This fires genuinely parallel CreateHold calls — each on its own
/// connection, released together — at one Seat and asserts exactly one wins.
///
/// It runs against a real PostgreSQL because the guarantee is about row-level
/// locking; SQLite would serialise every writer and the race could never occur
/// to be caught (ADR-0001). Against the naive, unlocked implementation this test
/// is red: several calls each see the Seat free and all append Held. The row
/// lock is what turns that into exactly one winner.
/// </summary>
[Collection(OrderingDatabaseCollection.Name)]
public class CreateHoldConcurrencyTests(OrderingDatabaseFixture fixture)
{
    private const int Contenders = 20;

    private static readonly DateTimeOffset Now = new(2026, 9, 4, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Exactly_one_of_many_parallel_holds_on_the_same_seat_wins()
    {
        await using var setup = fixture.NewDbContext();
        var flightId = Guid.NewGuid();
        var seatIds = await OrderingTestData.SeedFlightWithSeatsAsync(setup, flightId, count: 1);

        var clock = new TestClock(Now);

        // Every contender blocks a pool thread at the barrier at once, so the pool
        // must be able to hand out that many threads without waiting out its slow
        // injection rate — otherwise the barrier stalls rather than staging a race.
        EnsureThreadPoolCanRun(Contenders);

        // A barrier so every contender is poised at CreateHold before any is let
        // go — the race is staged, not hoped for.
        using var startLine = new Barrier(Contenders);

        var attempts = Enumerable.Range(0, Contenders).Select(_ => Task.Run(async () =>
        {
            await using var db = fixture.NewDbContext();
            var service = OrderingTestData.HoldServiceFor(db, clock);

            startLine.SignalAndWait();

            return await service.CreateHoldAsync(
                new CreateHoldRequest(flightId, seatIds, Guid.NewGuid(), PricePerSeat: 49.99m));
        })).ToArray();

        var results = await Task.WhenAll(attempts);

        var granted = results.OfType<CreateHoldResult.Granted>().Count();
        var conflicts = results.OfType<CreateHoldResult.Conflict>().Count();

        Assert.Equal(1, granted);
        Assert.Equal(Contenders - 1, conflicts);

        // The ledger itself must show one Held movement, not just the API replies:
        // a second Held here is a double hold that slipped past the counting above.
        await using var verify = fixture.NewDbContext();
        var heldMovements = await verify.SeatMovements
            .CountAsync(movement => movement.SeatId == seatIds[0] && movement.Type == SeatMovementType.Held);

        Assert.Equal(1, heldMovements);
    }

    /// <summary>
    /// Raises the pool's minimum worker count so the barrier's participants can
    /// all block at once. Without this the pool injects threads roughly one per
    /// second, so a barrier wider than the default minimum turns a sub-second
    /// race into a multi-second stall on a many-contender run.
    /// </summary>
    private static void EnsureThreadPoolCanRun(int contenders)
    {
        ThreadPool.GetMinThreads(out var workers, out var completionPorts);
        ThreadPool.SetMinThreads(Math.Max(workers, contenders), completionPorts);
    }
}
