using System.Security.Claims;
using FlashFlights.ServiceDefaults.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace FlashFlights.Ordering.Holds;

/// <summary>
/// The HTTP surface for Holds, reached through the gateway at
/// <c>/api/ordering/holds</c> (the gateway strips <c>/api/ordering</c> before
/// forwarding). The handler is thin: it turns the token's <c>sub</c> into the
/// Hold's owner and lets <see cref="IHoldService"/> make every decision, then
/// maps its three outcomes onto the status codes the SPA distinguishes.
/// </summary>
public static class HoldEndpoints
{
    public const string BasePath = "/holds";

    public static IEndpointRouteBuilder MapFlashFlightsHolds(this IEndpointRouteBuilder endpoints)
    {
        var holds = endpoints.MapGroup(BasePath);

        // The buyer is whoever the token says, not a body field: a caller must
        // not be able to hold Seats in someone else's name.
        holds.MapPost("/", CreateHoldAsync).RequireAuthorization();

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
}
