namespace FlashFlights.DemoData;

/// <summary>
/// The one description of what a fresh FlashFlights comes up holding: five
/// Flights spanning every sale state, the cabins they sell, who has claimed
/// which Seats, and which buyers are watching what.
///
/// <para>
/// Four services seed from this, each writing only the part of it their own
/// store owns — Catalog the Flights and their advisory SeatCounts, Ordering the
/// Seats and the movement ledger, Notifications the Watches and the inbox, the
/// gateway the buyers. None of them reads another's store, and none of them has
/// to: every shared identifier is derived from a name rather than generated
/// (<see cref="DemoIds"/>), so they arrive at the same world independently and
/// in any order.
/// </para>
///
/// <para>
/// Everything here is relative to the <c>anchor</c> each service passes — its own
/// clock at the moment it seeds. No rule anywhere compares a timestamp one
/// service wrote against one another wrote, so the stores disagreeing by a few
/// seconds costs nothing.
/// </para>
///
/// <para>
/// The one exception is the watch demo, and it is worth naming. Catalog stamps
/// that Flight's <c>SaleStartsAt</c> a short lead time after <em>its</em> anchor,
/// and Notifications seeds the Watch that is meant to fire on it. If
/// Notifications' anchor ever fell more than that lead time behind Catalog's, the
/// sale would be announced before the Watch existed and the alert would never
/// fire — not a broken store, but a demo quietly missing the thing it exists to
/// show. In practice both write SQLite locally and seed within a second of each
/// other; it would take Notifications' own store being unreachable for most of a
/// minute while Catalog's was not. <see cref="DemoSeedOptions.WatchDemoLeadTime"/>
/// is the margin, if a slow environment ever needs a wider one.
/// </para>
/// </summary>
public sealed record DemoWorld(
    IReadOnlyList<DemoUser> Users,
    IReadOnlyList<DemoFlight> Flights,
    IReadOnlyList<DemoWatch> Watches,
    IReadOnlyList<DemoNotification> Notifications)
{
    /// <summary>
    /// Six across, the narrow-body layout the seat map was drawn for. A demo
    /// world choice, not a domain rule — nothing in FlashFlights knows what a
    /// cabin looks like, only that a Seat has a row and a column.
    /// </summary>
    private const string Columns = "ABCDEF";

    /// <summary>
    /// How long a seeded Hold that already resolved had left to run. Only its
    /// shape matters — the Hold is long since Confirmed — but a resolved Hold
    /// whose TTL never made sense would read oddly in the ledger.
    /// </summary>
    private static readonly TimeSpan CheckoutWindow = TimeSpan.FromMinutes(10);

    /// <summary>How long after a Hold was granted the buyer finished paying.</summary>
    private static readonly TimeSpan TimeToConfirm = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Builds the world as of <paramref name="anchor"/> — the seeding service's
    /// own clock.
    /// </summary>
    public static DemoWorld Create(DateTimeOffset anchor, DemoSeedOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        // Live, and the showcase: the one Flight carrying a ReferenceFare on a
        // big saving, with a cabin showing all three Seat statuses at once.
        var lhrBcn = Flight(
            anchor, options,
            flightNumber: "FF104", origin: "LHR", destination: "BCN",
            departureAt: anchor.AddDays(2).AddHours(6),
            flashPrice: 149.00m,
            referenceFare: 229.00m,
            saleStartsAt: anchor.AddHours(-2).AddMinutes(-10),
            saleEndsAt: anchor.AddHours(5).AddMinutes(50),
            rows: 18,
            claims: [
                Booked(DemoUsers.Ada.Id, anchor.AddMinutes(-92), "12A", "12B"),
                Booked(DemoUsers.Grace.Id, anchor.AddMinutes(-55), "3D", "3E", "3F"),
                Booked(DemoUsers.Katherine.Id, anchor.AddMinutes(-40), "1A"),
                Holding(DemoUsers.Alan.Id, anchor.AddMinutes(-6), "7C", "7D"),
                Holding(DemoUsers.Grace.Id, anchor.AddMinutes(-3), "14F"),
            ]);

        // Upcoming, hours out, and untouched — the state a buyer sees before a
        // sale opens, and the plain-price path with no saving to show.
        var sfoJfk = Flight(
            anchor, options,
            flightNumber: "FF221", origin: "SFO", destination: "JFK",
            departureAt: anchor.AddDays(9),
            flashPrice: 329.00m,
            referenceFare: null,
            saleStartsAt: anchor.AddHours(6),
            saleEndsAt: anchor.AddHours(30),
            rows: 20,
            claims: []);

        // Ended: the window closed yesterday and the Flight has not left yet, so
        // it is still worth showing — browsable in full, bookable by no one.
        var cdgFco = Flight(
            anchor, options,
            flightNumber: "FF318", origin: "CDG", destination: "FCO",
            departureAt: anchor.AddHours(20),
            flashPrice: 89.00m,
            referenceFare: null,
            saleStartsAt: anchor.AddDays(-2),
            saleEndsAt: anchor.AddDays(-1).AddHours(-12),
            rows: 14,
            claims: [
                Booked(DemoUsers.Ada.Id, anchor.AddDays(-2).AddMinutes(20), "9C", "9D"),
                Booked(DemoUsers.Grace.Id, anchor.AddDays(-2).AddHours(3), "2A"),
                Booked(DemoUsers.Alan.Id, anchor.AddDays(-1).AddHours(-20), "5E", "5F", "6A"),
            ]);

        // Live and nearly gone: four Seats left of seventy-two, which is what
        // makes the "N left" badge and the race for the last Seats demonstrable.
        var amsLis = Flight(
            anchor, options,
            flightNumber: "FF442", origin: "AMS", destination: "LIS",
            departureAt: anchor.AddDays(3),
            flashPrice: 119.00m,
            referenceFare: null,
            saleStartsAt: anchor.AddMinutes(-55),
            saleEndsAt: anchor.AddHours(2).AddMinutes(5),
            rows: 12,
            claims: [
                .. SellOut(
                    flightNumber: "FF442",
                    rows: 12,
                    leaveUnsold: ["10A", "11F", "12C", "12D", "12E", "12F"],
                    from: anchor.AddMinutes(-54),
                    to: anchor.AddMinutes(-8)),
                Holding(DemoUsers.Alan.Id, anchor.AddMinutes(-4), "12C", "12D"),
            ]);

        // Upcoming, but only just: this is the Flight the watch demo runs on, so
        // its window opens seconds after the stores are seeded.
        var dubKef = Flight(
            anchor, options,
            flightNumber: "FF507", origin: "DUB", destination: "KEF",
            departureAt: anchor.AddDays(5),
            flashPrice: 99.00m,
            referenceFare: 169.00m,
            saleStartsAt: anchor + options.WatchDemoLeadTime,
            saleEndsAt: anchor + options.WatchDemoLeadTime + TimeSpan.FromHours(3),
            rows: 10,
            claims: []);

        var flights = new[] { lhrBcn, sfoJfk, cdgFco, amsLis, dubKef };

        // Ada's three: one still waiting, and two that fired when their sales
        // opened and left the Notifications below in her inbox. A Watch is never
        // cleared once it fires (CONTEXT.md), so all three are still here.
        IReadOnlyList<DemoWatch> watches =
        [
            Watch(DemoUsers.Ada, sfoJfk, anchor.AddHours(-4)),
            Watch(DemoUsers.Ada, lhrBcn, lhrBcn.SaleStartsAt.AddHours(-6)),
            Watch(DemoUsers.Ada, cdgFco, cdgFco.SaleStartsAt.AddHours(-5)),

            // So the watch list is not one buyer's alone.
            Watch(DemoUsers.Grace, sfoJfk, anchor.AddHours(-9)),

            // The one a reviewer sees fire, moments from now.
            Watch(DemoUsers.Katherine, dubKef, anchor.AddMinutes(-20)),
        ];

        // What those two fired Watches left behind: one from two days ago,
        // already read, and one from this sale's opening a couple of hours back,
        // still unread — so the inbox shows both states and the shell carries an
        // unread badge on a first look.
        IReadOnlyList<DemoNotification> notifications =
        [
            Notified(DemoUsers.Ada, cdgFco, cdgFco.SaleStartsAt, readAt: cdgFco.SaleStartsAt.AddMinutes(25)),
            Notified(DemoUsers.Ada, lhrBcn, lhrBcn.SaleStartsAt, readAt: null),
        ];

        return new DemoWorld(DemoUsers.All, flights, watches, notifications);
    }

    /// <summary>A claim that resolved into a Booking.</summary>
    private static Claim Booked(Guid buyerId, DateTimeOffset createdAt, params string[] seatNumbers) =>
        new(buyerId, createdAt, createdAt + TimeToConfirm, seatNumbers);

    /// <summary>A claim still in checkout — the Seats read as Held.</summary>
    private static Claim Holding(Guid buyerId, DateTimeOffset createdAt, params string[] seatNumbers) =>
        new(buyerId, createdAt, ConfirmedAt: null, seatNumbers);

    private static DemoWatch Watch(DemoUser watcher, DemoFlight flight, DateTimeOffset createdAt) =>
        new(DemoIds.Watch(watcher.Key, flight.FlightNumber), watcher, flight, createdAt);

    private static DemoNotification Notified(
        DemoUser recipient,
        DemoFlight flight,
        DateTimeOffset createdAt,
        DateTimeOffset? readAt) =>
        new(DemoIds.Notification(recipient.Key, flight.FlightNumber), recipient, flight, createdAt, readAt);

    /// <summary>
    /// Every Seat in the cabin except <paramref name="leaveUnsold"/>, dealt out
    /// as party-sized Bookings and spread across the window. Written rather than
    /// listed because sixty-six Seats named one by one would be a wall of text
    /// nobody would ever read, let alone check.
    ///
    /// <para>
    /// The buyers are other passengers rather than the demo accounts. A cabin
    /// this full needs roughly thirty buyers, and dealing it out among four
    /// would leave each of them holding eight separate bookings on one Flight —
    /// a booking history that reads like a bug. Nobody signs in as these, which
    /// is exactly right: on a real flight almost everyone aboard is someone
    /// whose account you cannot open.
    /// </para>
    /// </summary>
    private static IReadOnlyList<Claim> SellOut(
        string flightNumber,
        int rows,
        IReadOnlyList<string> leaveUnsold,
        DateTimeOffset from,
        DateTimeOffset to)
    {
        var unsold = leaveUnsold.ToHashSet(StringComparer.Ordinal);
        var forSale = SeatNumbers(rows).Where(seatNumber => !unsold.Contains(seatNumber)).ToArray();

        // Fixed, uneven party sizes so the cabin does not fill in a suspiciously
        // regular pattern. Cycled, so the result depends only on the cabin.
        int[] partySizes = [2, 1, 3, 2, 4, 1, 2, 3];

        var parties = new List<string[]>();
        for (var taken = 0; taken < forSale.Length;)
        {
            var size = Math.Min(partySizes[parties.Count % partySizes.Length], forSale.Length - taken);
            parties.Add(forSale[taken..(taken + size)]);
            taken += size;
        }

        // Spread evenly from the first sale to the last, so the ledger reads like
        // a sale filling up rather than everything landing at one instant.
        var step = parties.Count <= 1 ? TimeSpan.Zero : (to - from) / (parties.Count - 1);

        return [.. parties.Select((seats, index) => Booked(
            DemoIds.Passenger(flightNumber, index),
            from + step * index,
            seats))];
    }

    private static DemoFlight Flight(
        DateTimeOffset anchor,
        DemoSeedOptions options,
        string flightNumber,
        string origin,
        string destination,
        DateTimeOffset departureAt,
        decimal flashPrice,
        decimal? referenceFare,
        DateTimeOffset saleStartsAt,
        DateTimeOffset saleEndsAt,
        int rows,
        IReadOnlyList<Claim> claims)
    {
        var seats = SeatNumbers(rows)
            .Select(seatNumber => new DemoSeat(
                DemoIds.Seat(flightNumber, seatNumber),
                int.Parse(seatNumber[..^1]),
                seatNumber[^1..]))
            .ToArray();

        var byNumber = seats.ToDictionary(seat => seat.SeatNumber, StringComparer.Ordinal);
        var alreadyClaimed = new HashSet<string>(StringComparer.Ordinal);
        var holds = new List<DemoHold>(claims.Count);

        foreach (var claim in claims)
        {
            var claimed = claim.SeatNumbers
                .Select(seatNumber => byNumber.TryGetValue(seatNumber, out var seat)
                    ? seat
                    // A typo in a seat label would otherwise seed a Flight that
                    // quietly sells fewer Seats than its cabin holds.
                    : throw new InvalidOperationException(
                        $"Demo flight {flightNumber} has no seat {seatNumber}."))
                .ToArray();

            foreach (var seat in claimed)
            {
                if (!alreadyClaimed.Add(seat.SeatNumber))
                {
                    // Two claims on one Seat is precisely what this system exists
                    // to make impossible, so seeding it would be a demo that
                    // disproves its own point.
                    throw new InvalidOperationException(
                        $"Demo flight {flightNumber} claims seat {seat.SeatNumber} twice.");
                }
            }

            holds.Add(new DemoHold(
                DemoIds.Hold(flightNumber, claim.BuyerId, claim.SeatNumbers[0]),
                claim.BuyerId,
                claim.CreatedAt,
                // A resolved Hold keeps the TTL it actually ran under; a live one
                // is given a long leash so the expiry sweep leaves the Held Seats
                // on screen for the length of the demo.
                claim.ConfirmedAt is null ? anchor + options.HeldSeatTtl : claim.CreatedAt + CheckoutWindow,
                claim.ConfirmedAt,
                flashPrice,
                claimed));
        }

        return new DemoFlight(
            DemoIds.Flight(flightNumber),
            flightNumber,
            origin,
            destination,
            departureAt,
            flashPrice,
            referenceFare,
            saleStartsAt,
            saleEndsAt,
            seats,
            holds);
    }

    /// <summary>The cabin's Seat labels in the order a buyer reads them: 1A, 1B, … 18F.</summary>
    private static IEnumerable<string> SeatNumbers(int rows) =>
        from row in Enumerable.Range(1, rows)
        from column in Columns
        select $"{row}{column}";

    /// <summary>One claim before its Seat labels have been resolved against a cabin.</summary>
    private sealed record Claim(
        Guid BuyerId,
        DateTimeOffset CreatedAt,
        DateTimeOffset? ConfirmedAt,
        IReadOnlyList<string> SeatNumbers);
}
