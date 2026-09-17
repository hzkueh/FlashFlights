using FlashFlights.Catalog.Domain;
using FlashFlights.Catalog.Persistence;
using FlashFlights.DemoData;
using Microsoft.EntityFrameworkCore;

namespace FlashFlights.Catalog.Seeding;

/// <summary>
/// Catalog's share of the demo world: the Flights themselves, and the
/// <see cref="FlightSeatCounts"/> baseline the list page decorates them with.
///
/// <para>
/// The counts are written here rather than arriving as events, because that is
/// where they come from anyway — an event carries a delta, never a total, so
/// Catalog has always had to establish TotalSeats and the all-Available starting
/// point itself (see <c>SeatCountsProjector</c>). The demo world says exactly
/// which Seats Ordering will seed as Held and Confirmed, so this takes the same
/// answer from the same place instead of waiting for movements that are never
/// published for seeded rows.
/// </para>
/// </summary>
public sealed class CatalogDemoSeeder(CatalogDbContext db) : IDemoSeeder
{
    public async Task<bool> SeedAsync(DemoWorld world, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(world);

        // Any Flight at all, not just the demo ones: a catalog someone has
        // already put a Flight in is not one to pour a demo world into.
        if (await db.Flights.AnyAsync(cancellationToken))
        {
            return false;
        }

        foreach (var flight in world.Flights)
        {
            db.Flights.Add(new Flight
            {
                Id = flight.Id,
                FlightNumber = flight.FlightNumber,
                Origin = flight.Origin,
                Destination = flight.Destination,
                DepartureAt = flight.DepartureAt,
                FlashPrice = flight.FlashPrice,
                ReferenceFare = flight.ReferenceFare,
                SaleStartsAt = flight.SaleStartsAt,
                SaleEndsAt = flight.SaleEndsAt,

                // Left unhandled deliberately, including for the sales that have
                // already opened. Announcing a crossing is how Ordering learns
                // the window it needs to grant a Hold (ADR-0003) and how
                // Notifications learns the moment has passed (ADR-0002) — so a
                // seeded Flight marked as already settled would be one nobody
                // could ever buy a seat on. The scheduler picks these up on its
                // next scan and announces every one of them, closed windows
                // included; both consumers are idempotent, so the seeded inbox
                // and the seeded ledger absorb it without changing.
                SaleStartHandledAt = null,
            });

            db.FlightSeatCounts.Add(new FlightSeatCounts
            {
                FlightId = flight.Id,
                TotalSeats = flight.TotalSeats,
                AvailableSeats = flight.AvailableSeats,
                HeldSeats = flight.HeldSeats,
                ConfirmedSeats = flight.ConfirmedSeats,

                // The newest movement these counts already reflect. Seeded
                // movements are all in the past, so the first real one a buyer
                // causes is newer and is folded in rather than dropped as a
                // straggler.
                LastMovementAt = flight.LastMovementAt,
            });
        }

        // One save for the whole world: EF wraps it in a transaction, which is
        // what makes a seed that fails partway leave nothing behind for the
        // retry to mistake for a catalog someone else filled.
        await db.SaveChangesAsync(cancellationToken);

        return true;
    }
}
