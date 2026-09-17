using FlashFlights.Contracts;
using FlashFlights.Ordering.Sales;
using Microsoft.EntityFrameworkCore;

namespace FlashFlights.Ordering.Tests;

/// <summary>
/// How Ordering comes to know a Flight's sale window at all: it consumes the same
/// <see cref="FlightSaleStarted"/> Notifications does and keeps the window end
/// the announcement carries (ADR-0003). No call to Catalog, at Hold time or any
/// other — the row this writes is the whole of what
/// <see cref="Ordering.Domain.SaleWindowRules"/> later reads.
/// </summary>
[Collection(OrderingDatabaseCollection.Name)]
public class SaleWindowRecorderTests(OrderingDatabaseFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 9, 15, 10, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset SaleEndsAt = new(2026, 9, 15, 14, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Records_the_window_the_announcement_carries()
    {
        await using var db = fixture.NewDbContext();
        var flightId = Guid.NewGuid();

        await new SaleWindowRecorder(db, new TestClock(Now))
            .OnFlightSaleStartedAsync(AnnouncementFor(flightId));

        var recorded = await db.SaleAnnouncements.SingleAsync(a => a.FlightId == flightId);
        Assert.Equal(SaleEndsAt, recorded.SaleEndsAt);

        // This service's own clock, not Catalog's: when the announcement landed
        // here is bookkeeping, and only SaleEndsAt is ever the rule.
        Assert.Equal(Now, recorded.AnnouncedAt);
    }

    /// <summary>
    /// <see cref="FlightSaleStarted"/> is published at-least-once (ADR-0002), so
    /// a second delivery must find the row already there and leave it alone
    /// rather than fail the consumer and send a real announcement to the error
    /// queue.
    /// </summary>
    [Fact]
    public async Task A_redelivered_announcement_changes_nothing()
    {
        await using var db = fixture.NewDbContext();
        var flightId = Guid.NewGuid();
        var recorder = new SaleWindowRecorder(db, new TestClock(Now));

        await recorder.OnFlightSaleStartedAsync(AnnouncementFor(flightId));
        await recorder.OnFlightSaleStartedAsync(AnnouncementFor(flightId));

        var recorded = await db.SaleAnnouncements.SingleAsync(a => a.FlightId == flightId);
        Assert.Equal(Now, recorded.AnnouncedAt);
    }

    /// <summary>
    /// One sale window per Flight (CONTEXT.md), announced once for the life of
    /// the system — so a second announcement naming a different end is a
    /// redelivery that has been reshaped somewhere, not a window being moved.
    /// First write wins, and the Holds already granted against the recorded
    /// window keep their meaning.
    /// </summary>
    [Fact]
    public async Task A_later_announcement_does_not_move_a_window_already_recorded()
    {
        await using var db = fixture.NewDbContext();
        var flightId = Guid.NewGuid();
        var recorder = new SaleWindowRecorder(db, new TestClock(Now));

        await recorder.OnFlightSaleStartedAsync(AnnouncementFor(flightId));
        await recorder.OnFlightSaleStartedAsync(AnnouncementFor(flightId) with { SaleEndsAt = SaleEndsAt.AddDays(1) });

        var recorded = await db.SaleAnnouncements.SingleAsync(a => a.FlightId == flightId);
        Assert.Equal(SaleEndsAt, recorded.SaleEndsAt);
    }

    /// <summary>
    /// Catalog announces every crossing, including a window that had already
    /// closed when it was first seen — an already-ended sale in the seed data
    /// (ADR-0002). Ordering records it all the same: the row is what lets the
    /// Hold rule say "ended" rather than "never heard of", and both refuse
    /// anyway.
    /// </summary>
    [Fact]
    public async Task An_already_closed_window_is_still_recorded()
    {
        await using var db = fixture.NewDbContext();
        var flightId = Guid.NewGuid();

        await new SaleWindowRecorder(db, new TestClock(SaleEndsAt.AddHours(1)))
            .OnFlightSaleStartedAsync(AnnouncementFor(flightId));

        Assert.True(await db.SaleAnnouncements.AnyAsync(a => a.FlightId == flightId));
    }

    private static FlightSaleStarted AnnouncementFor(Guid flightId) =>
        new(flightId, "FF210", "SFO", "JFK", SaleEndsAt, Now);
}
