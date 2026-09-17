using System.Text.RegularExpressions;
using FlashFlights.DemoData;

namespace FlashFlights.DemoData.Tests;

/// <summary>
/// Ticket 11's acceptance criteria, asserted against the demo world itself
/// rather than against any one store. Every service seeds a projection of this,
/// so "is the live sale demonstrable, is a saving shown, is something nearly
/// sold out" is answerable here once instead of four times.
/// </summary>
public class DemoWorldTests
{
    private static readonly DateTimeOffset Anchor = new(2026, 9, 17, 9, 0, 0, TimeSpan.Zero);

    private static DemoWorld World(DemoSeedOptions? options = null) =>
        DemoWorld.Create(Anchor, options ?? new DemoSeedOptions());

    [Fact]
    public void Spans_upcoming_live_and_ended_sales()
    {
        var flights = World().Flights;

        Assert.Contains(flights, flight => Anchor < flight.SaleStartsAt);
        Assert.Contains(flights, flight => flight.SaleStartsAt <= Anchor && Anchor < flight.SaleEndsAt);
        Assert.Contains(flights, flight => flight.SaleEndsAt <= Anchor);
    }

    /// <summary>
    /// "Realistic routes, departure times, and flash prices — not Flight 1,
    /// Flight 2." Carrier designators and airport codes rather than counters,
    /// every route distinct, and every departure still ahead of the seed.
    /// </summary>
    [Fact]
    public void Reads_like_a_real_airline_rather_than_a_counter()
    {
        var flights = World().Flights;

        Assert.All(flights, flight =>
        {
            Assert.Matches(new Regex("^FF[0-9]{3}$"), flight.FlightNumber);
            Assert.Matches(new Regex("^[A-Z]{3}$"), flight.Origin);
            Assert.Matches(new Regex("^[A-Z]{3}$"), flight.Destination);
            Assert.NotEqual(flight.Origin, flight.Destination);
            Assert.True(flight.DepartureAt > Anchor, $"{flight.FlightNumber} has already departed.");
            Assert.True(flight.FlashPrice > 0, $"{flight.FlightNumber} is free.");
        });

        Assert.Distinct(flights.Select(flight => flight.FlightNumber));
        Assert.Distinct(flights.Select(flight => $"{flight.Origin}-{flight.Destination}"));
    }

    /// <summary>
    /// A Flight's sale opens before it departs, and the two other orderings a
    /// window has to respect.
    /// </summary>
    [Fact]
    public void Every_sale_window_opens_before_it_closes_and_closes_before_departure()
    {
        Assert.All(World().Flights, flight =>
        {
            Assert.True(flight.SaleStartsAt < flight.SaleEndsAt, $"{flight.FlightNumber}'s window is inside out.");
            Assert.True(flight.SaleEndsAt <= flight.DepartureAt, $"{flight.FlightNumber} sells seats after it leaves.");
        });
    }

    /// <summary>
    /// Both price paths are demonstrable, and ReferenceFare stays the exception
    /// rather than the default (CONTEXT.md).
    /// </summary>
    [Fact]
    public void Some_flights_advertise_a_saving_and_most_do_not()
    {
        var flights = World().Flights;
        var marked = flights.Where(flight => flight.ReferenceFare is not null).ToList();

        Assert.NotEmpty(marked);
        Assert.Contains(flights, flight => flight.ReferenceFare is null);
        Assert.True(marked.Count * 2 <= flights.Count, "ReferenceFare is the exception, not the default.");
    }

    /// <summary>A ReferenceFare below the FlashPrice would advertise a negative saving.</summary>
    [Fact]
    public void A_reference_fare_is_always_above_the_flash_price_it_marks_down()
    {
        Assert.All(
            World().Flights.Where(flight => flight.ReferenceFare is not null),
            flight => Assert.True(
                flight.ReferenceFare > flight.FlashPrice,
                $"{flight.FlightNumber} claims a saving it does not offer."));
    }

    [Fact]
    public void Every_cabin_is_a_believable_size()
    {
        Assert.All(World().Flights, flight =>
        {
            Assert.InRange(flight.TotalSeats, 48, 300);
            Assert.Distinct(flight.Seats.Select(seat => seat.SeatNumber));
        });
    }

    /// <summary>The seat map has something to show in each of its three states.</summary>
    [Fact]
    public void Some_flight_shows_available_held_and_confirmed_seats_at_once()
    {
        Assert.Contains(
            World().Flights,
            flight => flight.AvailableSeats > 0 && flight.HeldSeats > 0 && flight.ConfirmedSeats > 0);
    }

    [Fact]
    public void One_flight_is_nearly_sold_out()
    {
        Assert.Contains(
            World().Flights,
            flight => flight.AvailableSeats is > 0 and <= 6 && flight.TotalSeats >= 48);
    }

    /// <summary>
    /// The watch demo: a sale still ahead when the world is seeded, and ahead by
    /// little enough to sit and watch it open.
    /// </summary>
    [Fact]
    public void One_sale_opens_within_a_minute_of_the_seed()
    {
        var imminent = World().Flights
            .Where(flight => flight.SaleStartsAt > Anchor)
            .MinBy(flight => flight.SaleStartsAt);

        Assert.NotNull(imminent);
        Assert.InRange(imminent.SaleStartsAt - Anchor, TimeSpan.Zero, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void The_imminent_sale_is_the_one_somebody_is_watching()
    {
        var world = World();
        var imminent = world.Flights.Where(flight => flight.SaleStartsAt > Anchor).MinBy(flight => flight.SaleStartsAt)!;

        Assert.Contains(world.Watches, watch => watch.Flight.Id == imminent.Id);
    }

    [Fact]
    public void The_watch_demo_lead_time_is_configurable()
    {
        var world = World(new DemoSeedOptions { WatchDemoLeadTime = TimeSpan.FromMinutes(10) });
        var imminent = world.Flights.Where(flight => flight.SaleStartsAt > Anchor).MinBy(flight => flight.SaleStartsAt)!;

        Assert.Equal(Anchor.AddMinutes(10), imminent.SaleStartsAt);
    }

    /// <summary>
    /// "Booking history and the inbox aren't empty on a fresh look" — and both
    /// belong to one account, so a reviewer only has to sign in once to see them.
    /// </summary>
    [Fact]
    public void One_demo_buyer_arrives_with_bookings_watches_and_an_inbox()
    {
        var world = World();

        var buyer = world.Users.Single(user => user.Key == DemoUsers.Ada.Key);

        Assert.Contains(
            world.Flights.SelectMany(flight => flight.Holds),
            hold => hold.IsConfirmed && hold.BuyerId == buyer.Id);
        Assert.Contains(world.Watches, watch => watch.Watcher.Id == buyer.Id);
        Assert.Contains(world.Notifications, notification => notification.Recipient.Id == buyer.Id);
    }

    /// <summary>
    /// A booking history a person could plausibly have. The nearly-sold-out
    /// cabin needs some thirty buyers, and dealing those among the four demo
    /// accounts would leave each holding eight separate bookings on one Flight —
    /// a history that reads like a bug rather than a demo.
    /// </summary>
    [Fact]
    public void No_demo_buyer_books_the_same_flight_more_than_once()
    {
        var world = World();
        var accounts = world.Users.Select(user => user.Id).ToHashSet();

        Assert.All(world.Flights, flight => Assert.Distinct(
            flight.Holds
                .Where(hold => hold.IsConfirmed && accounts.Contains(hold.BuyerId))
                .Select(hold => hold.BuyerId)));
    }

    /// <summary>
    /// Everyone else aboard is somebody whose account a reviewer cannot open,
    /// which is what nearly every passenger on a real flight is.
    /// </summary>
    [Fact]
    public void The_rest_of_a_full_cabin_was_bought_by_passengers_with_no_account()
    {
        var world = World();
        var accounts = world.Users.Select(user => user.Id).ToHashSet();

        var soldOut = world.Flights.Single(flight => flight.AvailableSeats <= 6 && flight.TotalSeats >= 48);
        var buyers = soldOut.Holds.Select(hold => hold.BuyerId).Distinct().ToList();

        Assert.True(buyers.Count(buyer => !accounts.Contains(buyer)) > 10, "one cabin, too few buyers.");
    }

    /// <summary>Both inbox states, so the unread badge and a read one are each visible.</summary>
    [Fact]
    public void The_seeded_inbox_holds_a_read_notification_and_an_unread_one()
    {
        var notifications = World().Notifications;

        Assert.Contains(notifications, notification => notification.ReadAt is null);
        Assert.Contains(notifications, notification => notification.ReadAt is not null);
    }

    /// <summary>
    /// A Watch is a subscription to a moment still ahead (CONTEXT.md), so every
    /// seeded one was created before its Flight's sale opened — including the two
    /// that have since fired.
    /// </summary>
    [Fact]
    public void Every_watch_was_created_while_its_sale_was_still_ahead()
    {
        Assert.All(World().Watches, watch => Assert.True(
            watch.CreatedAt < watch.Flight.SaleStartsAt,
            $"{watch.Watcher.Key} watched {watch.Flight.FlightNumber} after its sale had opened."));
    }

    /// <summary>
    /// A Notification exists because a Watch fired when the sale opened — so each one
    /// has a matching Watch, and is stamped no earlier than the moment it fired.
    /// </summary>
    [Fact]
    public void Every_seeded_notification_traces_to_a_watch_that_fired()
    {
        var world = World();

        Assert.All(world.Notifications, notification =>
        {
            Assert.Contains(
                world.Watches,
                watch => watch.Watcher.Id == notification.Recipient.Id && watch.Flight.Id == notification.Flight.Id);
            Assert.True(notification.CreatedAt >= notification.Flight.SaleStartsAt);
            Assert.True(notification.Flight.SaleStartsAt <= Anchor, "a notification fired for a sale that has not opened.");
        });
    }

    [Fact]
    public void Seat_counts_add_up_to_the_cabin()
    {
        Assert.All(World().Flights, flight => Assert.Equal(
            flight.TotalSeats,
            flight.AvailableSeats + flight.HeldSeats + flight.ConfirmedSeats));
    }

    /// <summary>
    /// The guarantee the whole system exists to prove. A seeded world that broke
    /// it would be a demo disproving its own point.
    /// </summary>
    [Fact]
    public void No_seat_is_claimed_twice()
    {
        Assert.All(World().Flights, flight => Assert.Distinct(
            flight.Holds.SelectMany(hold => hold.Seats).Select(seat => seat.Id)));
    }

    [Fact]
    public void Every_claimed_seat_belongs_to_the_cabin_that_sold_it()
    {
        Assert.All(World().Flights, flight =>
        {
            var cabin = flight.Seats.Select(seat => seat.Id).ToHashSet();

            Assert.All(
                flight.Holds.SelectMany(hold => hold.Seats),
                seat => Assert.Contains(seat.Id, cabin));
        });
    }

    /// <summary>
    /// Held Seats have to still read as Held: a seeded Hold already past its TTL
    /// computes back to Available (ADR-0001) and the state vanishes from the map.
    /// </summary>
    [Fact]
    public void Held_seats_outlast_the_demo()
    {
        var live = World().Flights.SelectMany(flight => flight.Holds).Where(hold => !hold.IsConfirmed).ToList();

        Assert.NotEmpty(live);
        Assert.All(live, hold => Assert.True(
            hold.ExpiresAt >= Anchor.AddHours(1),
            "a seeded Held seat expires before anyone could look at it."));
    }

    [Fact]
    public void Every_confirmed_hold_was_confirmed_inside_its_own_ttl()
    {
        Assert.All(
            World().Flights.SelectMany(flight => flight.Holds).Where(hold => hold.IsConfirmed),
            hold =>
            {
                Assert.InRange(hold.ConfirmedAt!.Value, hold.CreatedAt, hold.ExpiresAt);
                Assert.True(hold.ConfirmedAt <= Anchor, "a booking was confirmed in the future.");
            });
    }

    /// <summary>
    /// Seeded movements are all in the past, which is what keeps Catalog's
    /// high-water mark from swallowing the first real movement a buyer causes.
    /// </summary>
    [Fact]
    public void The_newest_seeded_movement_is_already_in_the_past()
    {
        Assert.All(World().Flights, flight => Assert.True(
            flight.LastMovementAt <= Anchor,
            $"{flight.FlightNumber}'s counts are dated ahead of the seed."));
    }

    [Fact]
    public void A_hold_is_priced_at_the_flash_price_it_claimed()
    {
        Assert.All(World().Flights, flight => Assert.All(
            flight.Holds,
            hold =>
            {
                Assert.Equal(flight.FlashPrice, hold.PricePerSeat);
                Assert.Equal(flight.FlashPrice * hold.Seats.Count, hold.PricePaid);
            }));
    }

    /// <summary>
    /// The property every seeder leans on: two services building the world from
    /// their own clocks still agree on every identifier, so the FlightId Catalog
    /// writes is the one Ordering seeds Seats against.
    /// </summary>
    [Fact]
    public void Identifiers_do_not_depend_on_when_the_world_was_built()
    {
        var early = DemoWorld.Create(Anchor, new DemoSeedOptions());
        var late = DemoWorld.Create(Anchor.AddDays(400), new DemoSeedOptions());

        Assert.Equal(Ids(early), Ids(late));

        static IReadOnlyList<Guid> Ids(DemoWorld world) =>
        [
            .. world.Users.Select(user => user.Id),
            .. world.Flights.Select(flight => flight.Id),
            .. world.Flights.SelectMany(flight => flight.Seats).Select(seat => seat.Id),
            .. world.Flights.SelectMany(flight => flight.Holds).Select(hold => hold.Id),
            .. world.Watches.Select(watch => watch.Id),
            .. world.Notifications.Select(notification => notification.Id),
        ];
    }

    [Fact]
    public void Every_identifier_in_the_world_is_distinct()
    {
        var world = World();

        Assert.Distinct(world.Users.Select(user => user.Id));
        Assert.Distinct(world.Flights.Select(flight => flight.Id));
        Assert.Distinct(world.Flights.SelectMany(flight => flight.Seats).Select(seat => seat.Id));
        Assert.Distinct(world.Flights.SelectMany(flight => flight.Holds).Select(hold => hold.Id));
        Assert.Distinct(world.Watches.Select(watch => watch.Id));
        Assert.Distinct(world.Notifications.Select(notification => notification.Id));
    }

    [Fact]
    public void Every_demo_buyer_has_a_distinct_address()
    {
        var users = World().Users;

        Assert.Distinct(users.Select(user => user.Email));
        Assert.All(users, user => Assert.EndsWith("@flashflights.test", user.Email, StringComparison.Ordinal));
    }
}
