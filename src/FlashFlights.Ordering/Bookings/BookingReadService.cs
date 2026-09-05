using FlashFlights.Ordering.Domain;
using FlashFlights.Ordering.Holds;
using FlashFlights.Ordering.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FlashFlights.Ordering.Bookings;

/// <summary>
/// Lists a buyer's Bookings straight from the store. Two reads, not a join in
/// SQL: the Bookings (each with its Hold's Flight id), then the Confirmed
/// SeatMovements that make up their Seats — because <see cref="Seat.SeatNumber"/>
/// is computed from two columns rather than stored, so the label a buyer reads
/// has to be formed in memory, the same way the grant and seat-map paths form it.
/// </summary>
public sealed class BookingReadService(OrderingDbContext db) : IBookingReadService
{
    public async Task<IReadOnlyList<BookingView>> ListBookingsForUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        // Newest first: a buyer scans their most recent purchase at the top. The
        // Flight id rides along from the Hold, the one place a Booking records it.
        var bookings = await db.Bookings
            .Where(booking => booking.UserId == userId)
            .OrderByDescending(booking => booking.ConfirmedAt)
            .Select(booking => new BookingRow(
                booking.Id,
                booking.HoldId,
                booking.UserId,
                booking.Hold!.FlightId,
                booking.ConfirmedAt,
                booking.PricePaid))
            .ToListAsync(cancellationToken);

        if (bookings.Count == 0)
        {
            return [];
        }

        var holdIds = bookings.Select(booking => booking.HoldId).ToArray();

        // A Booking's Seats are its Hold's Confirmed movements (ADR-0001) — which
        // Seat, on which Booking.
        var movements = await db.SeatMovements
            .Where(movement => holdIds.Contains(movement.HoldId) && movement.Type == SeatMovementType.Confirmed)
            .Select(movement => new { movement.HoldId, movement.SeatId })
            .ToListAsync(cancellationToken);

        // The whole Seat is loaded so the label is read from Seat.SeatNumber, the
        // computed property the grant and seat-map paths also reuse — never a copy
        // of its format, which a change to seat numbering could leave out of step.
        var seats = await db.Seats
            .Where(seat => movements.Select(movement => movement.SeatId).Contains(seat.Id))
            .ToDictionaryAsync(seat => seat.Id, cancellationToken);

        var seatsByHold = movements
            .GroupBy(movement => movement.HoldId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .Select(movement => seats[movement.SeatId])
                    // Grid order, so a Booking lists 1A, 1B, … as a buyer reads a cabin.
                    .OrderBy(seat => seat.RowNumber)
                    .ThenBy(seat => seat.ColumnLetter)
                    .Select(seat => new BookedSeatView(seat.Id, seat.SeatNumber))
                    .ToArray());

        return bookings
            .Select(booking => new BookingView(
                booking.BookingId,
                booking.HoldId,
                booking.UserId,
                booking.FlightId,
                booking.ConfirmedAt,
                booking.PricePaid,
                seatsByHold.TryGetValue(booking.HoldId, out var seats) ? seats : []))
            .ToArray();
    }

    private sealed record BookingRow(
        Guid BookingId,
        Guid HoldId,
        Guid UserId,
        Guid FlightId,
        DateTimeOffset ConfirmedAt,
        decimal PricePaid);
}
