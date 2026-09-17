using FlashFlights.DemoData;
using FlashFlights.Ordering.Domain;
using FlashFlights.Ordering.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FlashFlights.Ordering.Seeding;

/// <summary>
/// Ordering's share of the demo world: the Seats each Flight sells, and the
/// ledger of what has happened to them.
///
/// <para>
/// Nothing writes a Seat's status, because there is nowhere to write one
/// (ADR-0001). A Held Seat is a Seat whose newest movement is a Held one under a
/// Hold that has not expired; a Confirmed Seat is one whose Hold resolved. So
/// the seed appends the movements those states are computed from — the same
/// rows a real Hold and a real confirmation would have left — and the seat map
/// derives the rest on its own.
/// </para>
///
/// <para>
/// No <c>SaleAnnouncement</c> is seeded, deliberately. Ordering learns a Flight's
/// window only from Catalog's announcement (ADR-0003), and Catalog seeds its
/// Flights with that crossing unsettled precisely so the announcement is made
/// for real. Writing the window here instead would seed Ordering into believing
/// something it was never told, and hide a broken announcement path behind data
/// that happens to look right.
/// </para>
/// </summary>
public sealed class OrderingDemoSeeder(OrderingDbContext db) : IDemoSeeder
{
    public async Task<bool> SeedAsync(DemoWorld world, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(world);

        // Seats, not movements or announcements: an announcement can arrive
        // before this runs, and an empty ledger over a seeded cabin is a
        // perfectly ordinary state. Seats are what this seeder puts there.
        if (await db.Seats.AnyAsync(cancellationToken))
        {
            return false;
        }

        foreach (var flight in world.Flights)
        {
            db.Seats.AddRange(flight.Seats.Select(seat => new Seat
            {
                Id = seat.Id,
                FlightId = flight.Id,
                RowNumber = seat.Row,
                ColumnLetter = seat.Column,
            }));

            foreach (var hold in flight.Holds)
            {
                db.Holds.Add(new Hold
                {
                    Id = hold.Id,
                    FlightId = flight.Id,
                    UserId = hold.BuyerId,
                    CreatedAt = hold.CreatedAt,
                    ExpiresAt = hold.ExpiresAt,
                    PricePerSeat = hold.PricePerSeat,
                });

                foreach (var seat in hold.Seats)
                {
                    Append(hold.Id, seat.Id, SeatMovementType.Held, hold.CreatedAt);

                    if (hold.ConfirmedAt is { } confirmedAt)
                    {
                        Append(hold.Id, seat.Id, SeatMovementType.Confirmed, confirmedAt);
                    }
                }

                if (hold.ConfirmedAt is { } paidAt)
                {
                    db.Bookings.Add(new Booking
                    {
                        Id = DemoIds.Booking(hold.Id),
                        HoldId = hold.Id,
                        UserId = hold.BuyerId,
                        ConfirmedAt = paidAt,
                        PricePaid = hold.PricePaid,
                    });
                }
            }
        }

        // One save for the whole world, so a seed that fails partway leaves no
        // Seats behind for the retry to read as an already-seeded store. It is
        // also the only shape that works: EF orders the inserts from the
        // relationships, so movements land after the Seats and Holds they point
        // at without the seeder sequencing them by hand.
        await db.SaveChangesAsync(cancellationToken);

        return true;
    }

    private void Append(Guid holdId, Guid seatId, SeatMovementType type, DateTimeOffset occurredAt) =>
        db.SeatMovements.Add(new SeatMovement
        {
            Id = DemoIds.SeatMovement(holdId, seatId, type.ToString()),
            SeatId = seatId,
            HoldId = holdId,
            Type = type,
            OccurredAt = occurredAt,
        });
}
