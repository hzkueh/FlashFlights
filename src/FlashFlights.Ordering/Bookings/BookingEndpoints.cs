using System.Security.Claims;
using FlashFlights.Ordering.Holds;
using FlashFlights.ServiceDefaults.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace FlashFlights.Ordering.Bookings;

/// <summary>
/// The HTTP surface for booking history, reached through the gateway at
/// <c>/api/ordering/bookings</c> (the gateway strips <c>/api/ordering</c> before
/// forwarding). Unlike the seat map, this is authenticated: a Booking belongs to
/// one buyer, and the list is scoped to whoever the token says — never a body or
/// query field, so a caller cannot read someone else's purchases.
///
/// The wire shape is <see cref="BookingView"/>, the very shape a confirm returns,
/// so the SPA parses one Booking the same way whether it just made it or is
/// looking back at it.
/// </summary>
public static class BookingEndpoints
{
    public const string BasePath = "/bookings";

    public static IEndpointRouteBuilder MapFlashFlightsBookings(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(BasePath, ListBookingsAsync).RequireAuthorization();

        return endpoints;
    }

    private static async Task<Ok<IReadOnlyList<BookingView>>> ListBookingsAsync(
        ClaimsPrincipal principal,
        IBookingReadService bookings,
        CancellationToken cancellationToken)
    {
        var list = await bookings.ListBookingsForUserAsync(principal.RequireUserId(), cancellationToken);

        return TypedResults.Ok(list);
    }
}
