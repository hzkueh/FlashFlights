using FlashFlights.Ordering.Domain;
using FlashFlights.Ordering.Holds;
using FlashFlights.Ordering.Persistence;
using Microsoft.Extensions.Options;

namespace FlashFlights.Ordering.Tests;

/// <summary>
/// A clock the tests drive by hand, so an expiry can be reached in a test
/// without waiting out a real TTL. It stands in for the one TimeProvider the
/// read side and the sweep share in production.
/// </summary>
internal sealed class TestClock(DateTimeOffset now) : TimeProvider
{
    private DateTimeOffset _now = now;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}

/// <summary>Seeding helpers shared by the Ordering integration tests.</summary>
internal static class OrderingTestData
{
    public static HoldService HoldServiceFor(
        OrderingDbContext db,
        TimeProvider clock,
        TimeSpan? ttl = null,
        ISeatMovementNotifier? notifier = null) =>
        new(
            db,
            clock,
            Options.Create(new HoldOptions { Ttl = ttl ?? TimeSpan.FromMinutes(2) }),
            notifier ?? new NullSeatMovementNotifier());

    /// <summary>
    /// Seeds <paramref name="count"/> Seats on one Flight, all in row 1, and
    /// returns their ids in seat order. Seats are keyed by fresh Guids so two
    /// tests sharing the container never touch the same rows.
    /// </summary>
    public static async Task<Guid[]> SeedFlightWithSeatsAsync(OrderingDbContext db, Guid flightId, int count)
    {
        var seats = Enumerable.Range(0, count)
            .Select(index => new Seat
            {
                Id = Guid.NewGuid(),
                FlightId = flightId,
                RowNumber = 1,
                ColumnLetter = ((char)('A' + index)).ToString(),
            })
            .ToArray();

        db.Seats.AddRange(seats);
        await db.SaveChangesAsync();

        return [.. seats.Select(seat => seat.Id)];
    }

    /// <summary>
    /// Appends a resolved movement directly, for statuses a test needs to stage
    /// without the service that would normally produce them — a Confirmed Seat,
    /// say, before ConfirmHold exists.
    /// </summary>
    public static async Task AppendMovementAsync(
        OrderingDbContext db,
        Guid seatId,
        Guid flightId,
        SeatMovementType type,
        DateTimeOffset occurredAt)
    {
        var hold = new Hold
        {
            Id = Guid.CreateVersion7(),
            FlightId = flightId,
            UserId = Guid.NewGuid(),
            CreatedAt = occurredAt,
            ExpiresAt = occurredAt.AddMinutes(2),
            PricePerSeat = 49.99m,
        };

        db.Holds.Add(hold);
        db.SeatMovements.Add(new SeatMovement
        {
            Id = Guid.CreateVersion7(),
            SeatId = seatId,
            HoldId = hold.Id,
            Type = type,
            OccurredAt = occurredAt,
        });

        await db.SaveChangesAsync();
    }
}
