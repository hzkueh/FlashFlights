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
    /// A window no test's clock reaches, for the tests that are about the ledger
    /// rather than the sale: an open sale is the background they assume.
    /// </summary>
    private static readonly DateTimeOffset SaleNeverEnds = new(2100, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Seeds <paramref name="count"/> Seats on one Flight, all in row 1, and
    /// returns their ids in seat order. Seats are keyed by fresh Guids so two
    /// tests sharing the container never touch the same rows.
    ///
    /// <para>
    /// The Flight's sale is announced as open too, because Ordering refuses a
    /// Hold on a Flight it has heard no announcement for (ADR-0003) and every
    /// ledger test here is about Seats, not the window. Pass
    /// <paramref name="saleEndsAt"/> to seed a window a test intends to close;
    /// <see cref="SeedUnannouncedFlightWithSeatsAsync"/> is the opposite case.
    /// </para>
    /// </summary>
    public static Task<Guid[]> SeedFlightWithSeatsAsync(
        OrderingDbContext db,
        Guid flightId,
        int count,
        DateTimeOffset? saleEndsAt = null) =>
        SeedSeatsAsync(db, flightId, count, saleEndsAt ?? SaleNeverEnds);

    /// <summary>
    /// Seeds Seats on a Flight whose sale-start announcement has never reached
    /// Ordering — an Upcoming sale, or one whose announcement is still in flight.
    /// The case ADR-0003 decided to refuse.
    /// </summary>
    public static Task<Guid[]> SeedUnannouncedFlightWithSeatsAsync(
        OrderingDbContext db,
        Guid flightId,
        int count) =>
        SeedSeatsAsync(db, flightId, count, saleEndsAt: null);

    private static async Task<Guid[]> SeedSeatsAsync(
        OrderingDbContext db,
        Guid flightId,
        int count,
        DateTimeOffset? saleEndsAt)
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

        if (saleEndsAt is { } endsAt)
        {
            db.SaleAnnouncements.Add(new SaleAnnouncement
            {
                FlightId = flightId,
                SaleEndsAt = endsAt,
                AnnouncedAt = endsAt.AddHours(-1),
            });
        }

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
