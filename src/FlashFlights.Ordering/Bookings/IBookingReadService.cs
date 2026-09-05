using FlashFlights.Ordering.Holds;

namespace FlashFlights.Ordering.Bookings;

/// <summary>
/// The read side of Bookings: one buyer's completed purchases, newest first. A
/// plain read — a Booking is terminal (there are no cancellations or refunds,
/// CONTEXT.md), so unlike the seat map this needs no clock and computes nothing
/// that can change after it returns.
///
/// A Booking's Seats are its Hold's Confirmed SeatMovements, the same way a
/// Hold's Seats are its Held ones — there is no seat list of its own to read
/// (ADR-0001). Each Booking carries its Flight id but no Flight metadata:
/// Ordering owns no Flights, so the route and departure a buyer reads are
/// Catalog's to supply against that id.
/// </summary>
public interface IBookingReadService
{
    /// <summary>Every Booking belonging to the given User, most recently confirmed first.</summary>
    Task<IReadOnlyList<BookingView>> ListBookingsForUserAsync(Guid userId, CancellationToken cancellationToken = default);
}
