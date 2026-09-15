using FlashFlights.Ordering.Domain;
using FlashFlights.Ordering.Holds;
using Microsoft.EntityFrameworkCore;

namespace FlashFlights.Ordering.Tests;

/// <summary>
/// The rule ticket 13 asks for, kept where it belongs: a Hold is a claim on a
/// <em>flash price</em> (CONTEXT.md), so Ordering refuses one on a Flight whose
/// window is not open — rather than trusting the page that offered it to have
/// checked. Reproduced before this existed as a plain 201 on an ended sale.
///
/// <para>
/// Ordering knows the window only from the announcement it consumed (ADR-0003),
/// so these seed that row rather than any Flight: there is no Flight here to
/// read, which is the point.
/// </para>
/// </summary>
[Collection(OrderingDatabaseCollection.Name)]
public class CreateHoldSaleWindowTests(OrderingDatabaseFixture fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset SaleEndsAt = Now.AddHours(1);

    [Fact]
    public async Task Grants_a_hold_while_the_window_is_open()
    {
        await using var db = fixture.NewDbContext();
        var flightId = Guid.NewGuid();
        var seatIds = await OrderingTestData.SeedFlightWithSeatsAsync(db, flightId, count: 1, SaleEndsAt);

        var result = await OrderingTestData
            .HoldServiceFor(db, new TestClock(Now))
            .CreateHoldAsync(new CreateHoldRequest(flightId, seatIds, Guid.NewGuid(), 49.99m));

        Assert.IsType<CreateHoldResult.Granted>(result);
    }

    /// <summary>
    /// The reported bug: FF210's sale had ended days earlier and
    /// <c>POST /holds</c> still answered 201, so a Booking could be written
    /// against a sale that was over.
    /// </summary>
    [Fact]
    public async Task Refuses_a_hold_once_the_window_has_closed()
    {
        await using var db = fixture.NewDbContext();
        var flightId = Guid.NewGuid();
        var seatIds = await OrderingTestData.SeedFlightWithSeatsAsync(db, flightId, count: 1, SaleEndsAt);

        var result = await OrderingTestData
            .HoldServiceFor(db, new TestClock(SaleEndsAt.AddDays(2)))
            .CreateHoldAsync(new CreateHoldRequest(flightId, seatIds, Guid.NewGuid(), 49.99m));

        var refused = Assert.IsType<CreateHoldResult.SaleNotOpen>(result);
        Assert.Equal(SaleWindowState.Ended, refused.State);

        // Nothing reached the ledger: a refused Hold must not leave the Seat
        // looking held to the next reader.
        Assert.Equal(0, await db.SeatMovements.CountAsync(movement => movement.SeatId == seatIds[0]));
    }

    /// <summary>
    /// The other half of the same gap, and the one the SPA's gate could never
    /// close on its own: an Upcoming sale's price is not on offer yet. Ordering
    /// has heard no announcement for it — that is what upcoming means — which is
    /// exactly why "no window heard" has to refuse (ADR-0003).
    /// </summary>
    [Fact]
    public async Task Refuses_a_hold_on_a_flight_it_has_heard_no_announcement_for()
    {
        await using var db = fixture.NewDbContext();
        var flightId = Guid.NewGuid();
        var seatIds = await OrderingTestData.SeedUnannouncedFlightWithSeatsAsync(db, flightId, count: 1);

        var result = await OrderingTestData
            .HoldServiceFor(db, new TestClock(Now))
            .CreateHoldAsync(new CreateHoldRequest(flightId, seatIds, Guid.NewGuid(), 49.99m));

        var refused = Assert.IsType<CreateHoldResult.SaleNotOpen>(result);
        Assert.Equal(SaleWindowState.NotStarted, refused.State);
        Assert.Equal(0, await db.SeatMovements.CountAsync(movement => movement.SeatId == seatIds[0]));
    }

    /// <summary>
    /// The boundary Catalog and the SPA already use: at the instant the window
    /// closes the sale reads Ended on the page, so a Hold posted at that instant
    /// is refused rather than squeaking through.
    /// </summary>
    [Fact]
    public async Task Refuses_a_hold_posted_at_the_closing_instant()
    {
        await using var db = fixture.NewDbContext();
        var flightId = Guid.NewGuid();
        var seatIds = await OrderingTestData.SeedFlightWithSeatsAsync(db, flightId, count: 1, SaleEndsAt);

        var result = await OrderingTestData
            .HoldServiceFor(db, new TestClock(SaleEndsAt))
            .CreateHoldAsync(new CreateHoldRequest(flightId, seatIds, Guid.NewGuid(), 49.99m));

        Assert.IsType<CreateHoldResult.SaleNotOpen>(result);
    }

    /// <summary>
    /// A closed sale is the truth about the Flight, so it is what the buyer is
    /// told — offering "try other seats" for a sale that is over would send them
    /// back to a map where no seat can be held.
    /// </summary>
    [Fact]
    public async Task A_closed_window_is_reported_ahead_of_a_seat_someone_else_holds()
    {
        await using var db = fixture.NewDbContext();
        var flightId = Guid.NewGuid();
        var seatIds = await OrderingTestData.SeedFlightWithSeatsAsync(db, flightId, count: 1, SaleEndsAt);

        // Held while the window was open, then asked for again after it closed.
        var service = OrderingTestData.HoldServiceFor(db, new TestClock(Now));
        Assert.IsType<CreateHoldResult.Granted>(
            await service.CreateHoldAsync(new CreateHoldRequest(flightId, seatIds, Guid.NewGuid(), 49.99m)));

        var afterClose = await OrderingTestData
            .HoldServiceFor(db, new TestClock(SaleEndsAt.AddMinutes(1)))
            .CreateHoldAsync(new CreateHoldRequest(flightId, seatIds, Guid.NewGuid(), 49.99m));

        Assert.IsType<CreateHoldResult.SaleNotOpen>(afterClose);
    }

    /// <summary>
    /// ADR-0003's other half: the Hold <em>is</em> the claim, and it was granted
    /// while the price was on offer, so the window closing under a buyer
    /// mid-payment must not strand them. The TTL already bounds how far past the
    /// close this reaches, and it is the only thing that ends the Hold.
    /// </summary>
    [Fact]
    public async Task Confirming_a_hold_granted_inside_the_window_still_succeeds_after_it_closes()
    {
        await using var db = fixture.NewDbContext();
        var flightId = Guid.NewGuid();
        var seatIds = await OrderingTestData.SeedFlightWithSeatsAsync(db, flightId, count: 1, SaleEndsAt);
        var userId = Guid.NewGuid();

        // Held a minute before the close, with a TTL that outlives it.
        var clock = new TestClock(SaleEndsAt.AddMinutes(-1));
        var service = OrderingTestData.HoldServiceFor(db, clock, TimeSpan.FromMinutes(5));

        var granted = Assert.IsType<CreateHoldResult.Granted>(
            await service.CreateHoldAsync(new CreateHoldRequest(flightId, seatIds, userId, 49.99m)));

        // The window closes while the buyer is on the payment step.
        clock.Advance(TimeSpan.FromMinutes(2));

        var confirmed = Assert.IsType<ConfirmHoldResult.Confirmed>(
            await service.ConfirmHoldAsync(granted.Hold.HoldId, userId));

        Assert.Equal(49.99m, confirmed.Booking.PricePaid);
    }
}
