using FlashFlights.Catalog.Projection;

namespace FlashFlights.Catalog.Tests;

/// <summary>
/// The spec's second test target: feed the projector Ordering's movement events
/// and assert the cached counts move correctly and idempotently. The projector
/// is exercised directly here rather than through the bus — the consumers are
/// thin wrappers, and the projection logic is what needs proving.
/// </summary>
public class SeatCountsProjectorTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Holding_seats_moves_them_from_available_to_held()
    {
        using var testDb = CatalogTestDb.Create();
        var flightId = await testDb.SeedFlightAsync(totalSeats: 12);

        await ProjectorFor(testDb).ApplyHeldAsync(flightId, seatCount: 3, T0);

        var counts = await testDb.CountsAsync(flightId);
        Assert.Equal(9, counts.AvailableSeats);
        Assert.Equal(3, counts.HeldSeats);
        Assert.Equal(0, counts.ConfirmedSeats);
        Assert.Equal(12, counts.TotalSeats);
    }

    [Fact]
    public async Task Releasing_seats_returns_them_to_available()
    {
        using var testDb = CatalogTestDb.Create();
        var flightId = await testDb.SeedFlightAsync(totalSeats: 12);

        await ProjectorFor(testDb).ApplyHeldAsync(flightId, 3, T0);
        await ProjectorFor(testDb).ApplyReleasedAsync(flightId, 3, T0.AddSeconds(1));

        var counts = await testDb.CountsAsync(flightId);
        Assert.Equal(12, counts.AvailableSeats);
        Assert.Equal(0, counts.HeldSeats);
    }

    [Fact]
    public async Task Confirming_seats_moves_them_from_held_to_confirmed()
    {
        using var testDb = CatalogTestDb.Create();
        var flightId = await testDb.SeedFlightAsync(totalSeats: 12);

        await ProjectorFor(testDb).ApplyHeldAsync(flightId, 2, T0);
        await ProjectorFor(testDb).ApplyConfirmedAsync(flightId, 2, T0.AddSeconds(1));

        var counts = await testDb.CountsAsync(flightId);
        Assert.Equal(10, counts.AvailableSeats);
        Assert.Equal(0, counts.HeldSeats);
        Assert.Equal(2, counts.ConfirmedSeats);
    }

    /// <summary>
    /// A redelivered event carries the same OccurredAt it did the first time, so
    /// the high-water mark drops it — the whole reason the timestamp rides along.
    /// </summary>
    [Fact]
    public async Task A_redelivered_event_is_not_counted_twice()
    {
        using var testDb = CatalogTestDb.Create();
        var flightId = await testDb.SeedFlightAsync(totalSeats: 12);

        await ProjectorFor(testDb).ApplyHeldAsync(flightId, 3, T0);
        await ProjectorFor(testDb).ApplyHeldAsync(flightId, 3, T0);

        var counts = await testDb.CountsAsync(flightId);
        Assert.Equal(9, counts.AvailableSeats);
        Assert.Equal(3, counts.HeldSeats);
    }

    /// <summary>An event that arrives behind a newer one it was overtaken by is dropped, not un-done.</summary>
    [Fact]
    public async Task An_out_of_order_stale_event_is_dropped()
    {
        using var testDb = CatalogTestDb.Create();
        var flightId = await testDb.SeedFlightAsync(totalSeats: 12);

        await ProjectorFor(testDb).ApplyHeldAsync(flightId, 2, T0.AddSeconds(10));
        // Older than what we have already folded in — a straggler.
        await ProjectorFor(testDb).ApplyHeldAsync(flightId, 5, T0.AddSeconds(1));

        var counts = await testDb.CountsAsync(flightId);
        Assert.Equal(10, counts.AvailableSeats);
        Assert.Equal(2, counts.HeldSeats);
    }

    /// <summary>
    /// A movement for a Flight with no projection row yet (its baseline unseeded)
    /// is dropped rather than inventing counts from a total the event does not
    /// carry. The row's absence is not an error — seeding establishes it.
    /// </summary>
    [Fact]
    public async Task An_event_for_an_unseeded_flight_is_ignored()
    {
        using var testDb = CatalogTestDb.Create();
        var flightId = await testDb.SeedFlightAsync(totalSeats: null);

        await ProjectorFor(testDb).ApplyHeldAsync(flightId, 3, T0);

        await using var db = testDb.NewContext();
        Assert.False(db.FlightSeatCounts.Any(c => c.FlightId == flightId));
    }

    private static SeatCountsProjector ProjectorFor(CatalogTestDb testDb) => new(testDb.NewContext());
}
