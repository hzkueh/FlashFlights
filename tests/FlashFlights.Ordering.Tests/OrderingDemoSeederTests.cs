using FlashFlights.DemoData;
using FlashFlights.Ordering.Bookings;
using FlashFlights.Ordering.Domain;
using FlashFlights.Ordering.Persistence;
using FlashFlights.Ordering.Seeding;
using FlashFlights.Ordering.SeatMaps;
using Microsoft.EntityFrameworkCore;

namespace FlashFlights.Ordering.Tests;

/// <summary>
/// Ordering's half of ticket 11: a fresh clone's seat maps come up with a
/// believable mix of Seats, a demo buyer already has a booking history, and a
/// restart leaves both exactly as they were.
///
/// <para>
/// Asserted through the seat map rather than against the movement rows wherever
/// it can be, because a Seat's status is computed and not stored (ADR-0001) —
/// the rows are only right if the status they compute to is.
/// </para>
/// </summary>
[Collection(OrderingDatabaseCollection.Name)]
public class OrderingDemoSeederTests(OrderingDatabaseFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 9, 17, 9, 0, 0, TimeSpan.Zero);

    private static DemoWorld World() => DemoWorld.Create(Now, new DemoSeedOptions());

    [Fact]
    public async Task Seeds_every_flights_cabin()
    {
        var store = await fixture.NewEmptyStoreAsync();

        Assert.True(await SeedAsync(store));

        await using var db = store();

        foreach (var flight in World().Flights)
        {
            Assert.Equal(
                flight.TotalSeats,
                await db.Seats.CountAsync(seat => seat.FlightId == flight.Id));
        }
    }

    /// <summary>
    /// The mix the ticket asks for, read the way a buyer reads it. Held is the
    /// one that needs the clock: a seeded Hold past its TTL would compute back
    /// to Available and the state would be missing from the map.
    /// </summary>
    [Fact]
    public async Task The_seat_map_shows_the_mix_the_demo_world_describes()
    {
        var store = await fixture.NewEmptyStoreAsync();
        await SeedAsync(store);

        await using var db = store();
        var seatMaps = new SeatMapService(db, new TestClock(Now));

        foreach (var flight in World().Flights)
        {
            var map = await seatMaps.GetSeatMapAsync(flight.Id);
            var byStatus = map.Seats.CountBy(seat => seat.Status).ToDictionary();

            Assert.Equal(flight.AvailableSeats, byStatus.GetValueOrDefault(SeatStatus.Available));
            Assert.Equal(flight.HeldSeats, byStatus.GetValueOrDefault(SeatStatus.Held));
            Assert.Equal(flight.ConfirmedSeats, byStatus.GetValueOrDefault(SeatStatus.Confirmed));
        }
    }

    [Fact]
    public async Task Some_flight_shows_all_three_seat_statuses_at_once()
    {
        var store = await fixture.NewEmptyStoreAsync();
        await SeedAsync(store);

        await using var db = store();
        var seatMaps = new SeatMapService(db, new TestClock(Now));

        var statuses = new List<HashSet<SeatStatus>>();

        // One at a time: these all share the context, and a DbContext serves one
        // operation at a time.
        foreach (var flight in World().Flights)
        {
            var map = await seatMaps.GetSeatMapAsync(flight.Id);
            statuses.Add([.. map.Seats.Select(seat => seat.Status)]);
        }

        Assert.Contains(statuses, seen => seen.Count == 3);
    }

    /// <summary>
    /// The exact Seats the demo world names, not merely the right number of
    /// them — a seat map with the tally right and the labels wrong would look
    /// fine and read as a different cabin.
    /// </summary>
    [Fact]
    public async Task The_right_seats_are_the_held_ones()
    {
        var world = World();
        var store = await fixture.NewEmptyStoreAsync();
        await SeedAsync(store);

        await using var db = store();
        var seatMaps = new SeatMapService(db, new TestClock(Now));

        foreach (var flight in world.Flights.Where(flight => flight.HeldSeats > 0))
        {
            var map = await seatMaps.GetSeatMapAsync(flight.Id);

            Assert.Equal(
                flight.Holds
                    .Where(hold => !hold.IsConfirmed)
                    .SelectMany(hold => hold.Seats)
                    .Select(seat => seat.SeatNumber)
                    .Order(),
                map.Seats
                    .Where(seat => seat.Status == SeatStatus.Held)
                    .Select(seat => seat.SeatNumber)
                    .Order());
        }
    }

    /// <summary>"Booking history isn't empty on a fresh look."</summary>
    [Fact]
    public async Task A_demo_buyer_arrives_with_a_booking_history()
    {
        var world = World();
        var store = await fixture.NewEmptyStoreAsync();
        await SeedAsync(store);

        await using var db = store();
        var bookings = await new BookingReadService(db).ListBookingsForUserAsync(DemoUsers.Ada.Id);

        var expected = world.Flights
            .SelectMany(flight => flight.Holds)
            .Where(hold => hold.IsConfirmed && hold.BuyerId == DemoUsers.Ada.Id)
            .ToList();

        Assert.Equal(expected.Count, bookings.Count);
        Assert.Equal(
            expected.SelectMany(hold => hold.Seats).Select(seat => seat.SeatNumber).Order(),
            bookings.SelectMany(booking => booking.Seats).Select(seat => seat.SeatNumber).Order());
        Assert.All(bookings, booking => Assert.True(booking.PricePaid > 0));
    }

    /// <summary>
    /// A Confirmed Seat's history is a Held that became a Confirmed, because
    /// that is the only way a real one is ever reached. Seeding the confirmation
    /// alone would leave a ledger that no sequence of buyer actions could
    /// produce.
    /// </summary>
    [Fact]
    public async Task Every_confirmed_seat_was_held_before_it_was_confirmed()
    {
        var store = await fixture.NewEmptyStoreAsync();
        await SeedAsync(store);

        await using var db = store();
        var movements = await db.SeatMovements.ToListAsync();

        var confirmed = movements.Where(movement => movement.Type == SeatMovementType.Confirmed);

        Assert.All(confirmed, confirmation => Assert.Contains(
            movements,
            earlier => earlier.SeatId == confirmation.SeatId
                && earlier.HoldId == confirmation.HoldId
                && earlier.Type == SeatMovementType.Held
                && earlier.OccurredAt <= confirmation.OccurredAt));
    }

    [Fact]
    public async Task Every_seeded_booking_belongs_to_a_hold_that_was_confirmed()
    {
        var store = await fixture.NewEmptyStoreAsync();
        await SeedAsync(store);

        await using var db = store();

        var confirmedHolds = await db.SeatMovements
            .Where(movement => movement.Type == SeatMovementType.Confirmed)
            .Select(movement => movement.HoldId)
            .Distinct()
            .ToListAsync();

        Assert.Equal(
            confirmedHolds.Order(),
            (await db.Bookings.Select(booking => booking.HoldId).ToListAsync()).Order());
    }

    /// <summary>
    /// Ordering learns a sale window only from Catalog's announcement (ADR-0003).
    /// Seeding one here would let a broken announcement path pass unnoticed
    /// behind data that happens to look right.
    /// </summary>
    [Fact]
    public async Task Does_not_pretend_to_have_heard_an_announcement_it_was_never_sent()
    {
        var store = await fixture.NewEmptyStoreAsync();
        await SeedAsync(store);

        await using var db = store();

        Assert.Empty(await db.SaleAnnouncements.ToListAsync());
    }

    [Fact]
    public async Task A_second_run_over_a_seeded_store_changes_nothing()
    {
        var store = await fixture.NewEmptyStoreAsync();
        await SeedAsync(store);

        var before = await SnapshotAsync(store);

        Assert.False(await SeedAsync(store));
        Assert.Equal(before, await SnapshotAsync(store));
    }

    [Fact]
    public async Task Leaves_a_store_that_already_has_seats_alone()
    {
        var store = await fixture.NewEmptyStoreAsync();

        await using (var db = store())
        {
            await OrderingTestData.SeedFlightWithSeatsAsync(db, Guid.NewGuid(), count: 3);
        }

        Assert.False(await SeedAsync(store));

        await using var after = store();
        Assert.Equal(3, await after.Seats.CountAsync());
    }

    private static async Task<bool> SeedAsync(Func<OrderingDbContext> store)
    {
        await using var db = store();

        return await new OrderingDemoSeeder(db).SeedAsync(World());
    }

    /// <summary>Every row a reseed could have added or rewritten.</summary>
    private static async Task<string> SnapshotAsync(Func<OrderingDbContext> store)
    {
        await using var db = store();

        var seats = await db.Seats.ToListAsync();
        var holds = await db.Holds.ToListAsync();
        var movements = await db.SeatMovements.ToListAsync();
        var bookings = await db.Bookings.ToListAsync();

        return string.Join(
            "\n",
            seats.Select(seat => $"seat|{seat.Id}|{seat.FlightId}|{seat.SeatNumber}")
                .Concat(holds.Select(hold =>
                    $"hold|{hold.Id}|{hold.UserId}|{hold.CreatedAt:O}|{hold.ExpiresAt:O}|{hold.PricePerSeat}"))
                .Concat(movements.Select(movement =>
                    $"movement|{movement.Id}|{movement.SeatId}|{movement.Type}|{movement.OccurredAt:O}"))
                .Concat(bookings.Select(booking =>
                    $"booking|{booking.Id}|{booking.HoldId}|{booking.ConfirmedAt:O}|{booking.PricePaid}"))
                .Order(StringComparer.Ordinal));
    }
}
