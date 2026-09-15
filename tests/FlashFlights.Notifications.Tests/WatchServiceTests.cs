using FlashFlights.Notifications.Watching;

namespace FlashFlights.Notifications.Tests;

/// <summary>
/// Watching and un-watching (ticket 09, items 1 and 7). A Watch names one User
/// and one Flight, is idempotent, and only stands while the sale is still ahead
/// — the three things the endpoint above this service leans on.
/// </summary>
public class WatchServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_watched_flight_comes_back_in_the_users_watch_list()
    {
        using var testDb = NotificationsTestDb.Create();
        var userId = Guid.NewGuid();
        var flightId = Guid.NewGuid();

        Assert.Equal(WatchOutcome.Watched, await ServiceFor(testDb).WatchAsync(userId, flightId));

        Assert.Equal([flightId], await ServiceFor(testDb).ListWatchedFlightIdsAsync(userId));
    }

    /// <summary>
    /// A double-click is one subscription, not two — and not an error either. The
    /// store enforces it with a unique index; this is the caller-facing half.
    /// </summary>
    [Fact]
    public async Task Watching_twice_is_the_same_subscription()
    {
        using var testDb = NotificationsTestDb.Create();
        var userId = Guid.NewGuid();
        var flightId = Guid.NewGuid();

        await ServiceFor(testDb).WatchAsync(userId, flightId);
        Assert.Equal(WatchOutcome.Watched, await ServiceFor(testDb).WatchAsync(userId, flightId));

        Assert.Equal([flightId], await ServiceFor(testDb).ListWatchedFlightIdsAsync(userId));
    }

    [Fact]
    public async Task Un_watching_removes_the_subscription()
    {
        using var testDb = NotificationsTestDb.Create();
        var userId = Guid.NewGuid();
        var flightId = Guid.NewGuid();

        await ServiceFor(testDb).WatchAsync(userId, flightId);
        await ServiceFor(testDb).UnwatchAsync(userId, flightId);

        Assert.Empty(await ServiceFor(testDb).ListWatchedFlightIdsAsync(userId));
    }

    /// <summary>
    /// "Stop notifying me" is the same request whether or not a Watch was there
    /// — so un-watching something unwatched is a no-op, not a failure.
    /// </summary>
    [Fact]
    public async Task Un_watching_something_that_was_not_watched_is_harmless()
    {
        using var testDb = NotificationsTestDb.Create();

        await ServiceFor(testDb).UnwatchAsync(Guid.NewGuid(), Guid.NewGuid());
    }

    /// <summary>A Watch belongs to the User who made it; nobody else can see or clear it.</summary>
    [Fact]
    public async Task One_users_watches_are_not_anothers()
    {
        using var testDb = NotificationsTestDb.Create();
        var mine = Guid.NewGuid();
        var theirs = Guid.NewGuid();
        var flightId = Guid.NewGuid();

        await ServiceFor(testDb).WatchAsync(mine, flightId);

        Assert.Empty(await ServiceFor(testDb).ListWatchedFlightIdsAsync(theirs));

        await ServiceFor(testDb).UnwatchAsync(theirs, flightId);
        Assert.Equal([flightId], await ServiceFor(testDb).ListWatchedFlightIdsAsync(mine));
    }

    /// <summary>
    /// A Watch fires once, when the sale opens (CONTEXT.md). Once that moment has
    /// passed there is nothing left for a new Watch to wait for, so it is refused
    /// rather than accepted into a silence.
    /// </summary>
    [Fact]
    public async Task A_sale_that_has_already_opened_cannot_be_watched()
    {
        using var testDb = NotificationsTestDb.Create();
        var flightId = Guid.NewGuid();
        await testDb.RecordSaleAnnouncedAsync(flightId);

        Assert.Equal(
            WatchOutcome.SaleAlreadyStarted,
            await ServiceFor(testDb).WatchAsync(Guid.NewGuid(), flightId));

        Assert.Empty(await ServiceFor(testDb).ListWatchedFlightIdsAsync(Guid.NewGuid()));
    }

    private static WatchService ServiceFor(NotificationsTestDb testDb) =>
        new(testDb.NewContext(), new FixedClock(Now));

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
