using System.Security.Claims;
using FlashFlights.ServiceDefaults.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace FlashFlights.Ordering.Holds;

/// <summary>
/// The HTTP surface for Holds, reached through the gateway at
/// <c>/api/ordering/holds</c> (the gateway strips <c>/api/ordering</c> before
/// forwarding). The handlers are thin: they turn the token's <c>sub</c> into the
/// caller and let <see cref="IHoldService"/> make every decision — granting a
/// Hold and confirming one into a Booking — then map each outcome onto the status
/// code the SPA distinguishes.
/// </summary>
public static class HoldEndpoints
{
    public const string BasePath = "/holds";

    /// <summary>Where a confirmed Hold's Booking is reported to live — the Created location a confirm returns.</summary>
    public const string BookingsBasePath = "/bookings";

    public static IEndpointRouteBuilder MapFlashFlightsHolds(this IEndpointRouteBuilder endpoints)
    {
        var holds = endpoints.MapGroup(BasePath);

        // The buyer is whoever the token says, not a body field: a caller must
        // not be able to hold Seats in someone else's name.
        holds.MapPost("/", CreateHoldAsync).RequireAuthorization();

        // Confirm is scoped to one Hold and carries no body — the id is the whole
        // request, and the owner comes from the token the same way it does above.
        holds.MapPost("/{holdId:guid}/confirm", ConfirmHoldAsync).RequireAuthorization();

        return endpoints;
    }

    /// <summary>
    /// The body a client sends. It carries no user id — that comes from the
    /// token — but it does carry the FlashPrice the buyer was shown, which
    /// Catalog owns and Ordering freezes onto the Hold rather than looking up.
    /// </summary>
    public sealed record CreateHoldBody(Guid FlightId, IReadOnlyList<Guid>? SeatIds, decimal PricePerSeat);

    private static async Task<IResult> CreateHoldAsync(
        CreateHoldBody body,
        ClaimsPrincipal principal,
        IHoldService holds,
        CancellationToken cancellationToken)
    {
        var request = new CreateHoldRequest(
            body.FlightId,
            body.SeatIds ?? [],
            principal.RequireUserId(),
            body.PricePerSeat);

        var result = await holds.CreateHoldAsync(request, cancellationToken);

        return result switch
        {
            CreateHoldResult.Granted granted =>
                TypedResults.Created($"{BasePath}/{granted.Hold.HoldId}", granted.Hold),
            CreateHoldResult.Malformed malformed =>
                TypedResults.ValidationProblem(malformed.Errors),
            CreateHoldResult.Conflict conflict =>
                Conflict(conflict),
            _ => throw new InvalidOperationException($"Unhandled hold result: {result.GetType().Name}."),
        };
    }

    /// <summary>
    /// 409, distinct from the 400 a malformed request gets: the request was
    /// well-formed and simply lost the race, so the SPA can offer a retry on
    /// other Seats rather than telling the buyer they typed something wrong. The
    /// blocking Seats travel in a problem-details extension.
    /// </summary>
    private static IResult Conflict(CreateHoldResult.Conflict conflict) =>
        TypedResults.Problem(
            title: "Seats no longer available",
            detail: $"These seats are already held or booked: "
                + $"{string.Join(", ", conflict.Seats.Select(seat => seat.SeatNumber))}.",
            statusCode: StatusCodes.Status409Conflict,
            extensions: new Dictionary<string, object?> { ["seats"] = conflict.Seats });

    private static async Task<IResult> ConfirmHoldAsync(
        Guid holdId,
        ClaimsPrincipal principal,
        IHoldService holds,
        CancellationToken cancellationToken)
    {
        var result = await holds.ConfirmHoldAsync(holdId, principal.RequireUserId(), cancellationToken);

        return result switch
        {
            ConfirmHoldResult.Confirmed confirmed =>
                TypedResults.Created($"{BookingsBasePath}/{confirmed.Booking.BookingId}", confirmed.Booking),
            ConfirmHoldResult.NotFound =>
                TypedResults.NotFound(),
            ConfirmHoldResult.Expired =>
                Expired(),
            ConfirmHoldResult.AlreadyConfirmed =>
                AlreadyConfirmed(),
            _ => throw new InvalidOperationException($"Unhandled confirm result: {result.GetType().Name}."),
        };
    }

    /// <summary>
    /// 409 keyed to the retry-able case: the TTL lapsed before payment, so the
    /// Seats are likely free again and starting a fresh Hold may still win them.
    /// A <c>reason</c> extension lets the SPA branch without parsing prose.
    /// </summary>
    private static IResult Expired() =>
        TypedResults.Problem(
            title: "Hold expired",
            detail: "This hold reached its time limit before it was confirmed. "
                + "Its seats may be available again — try holding them afresh.",
            statusCode: StatusCodes.Status409Conflict,
            extensions: new Dictionary<string, object?> { ["reason"] = "expired" });

    /// <summary>
    /// 409 for the terminal case: the Hold already became a Booking, so there is
    /// nothing to retry. The matching <c>reason</c> tells the SPA to stop rather
    /// than offer another attempt.
    /// </summary>
    private static IResult AlreadyConfirmed() =>
        TypedResults.Problem(
            title: "Hold already confirmed",
            detail: "This hold has already been confirmed into a booking.",
            statusCode: StatusCodes.Status409Conflict,
            extensions: new Dictionary<string, object?> { ["reason"] = "alreadyConfirmed" });
}
