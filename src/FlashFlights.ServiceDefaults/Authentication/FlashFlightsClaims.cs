using System.Security.Claims;

namespace FlashFlights.ServiceDefaults.Authentication;

/// <summary>
/// What a FlashFlights token says about its bearer, and how a service reads it
/// back. Every service needs the User's id — Ordering to own a Hold,
/// Notifications to address a Watch — so the claim name lives here rather than
/// as a string literal in each of them.
/// </summary>
public static class FlashFlightsClaims
{
    /// <summary>The User's id. Raw <c>sub</c>, not the ClaimTypes URI it is normally mapped to.</summary>
    public const string UserId = "sub";

    /// <summary>The User's email, so a signed-in client can show who it is without another call.</summary>
    public const string Email = "email";

    /// <summary>
    /// The bearer's User id, or null when the request is unauthenticated or the
    /// token carries something that is not an id.
    /// </summary>
    public static Guid? GetUserId(this ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirst(UserId)?.Value, out var userId) ? userId : null;

    /// <summary>
    /// The bearer's User id on an endpoint that requires authentication.
    /// Throws rather than returning <see cref="Guid.Empty"/>, so a missing
    /// <c>[Authorize]</c> surfaces as a failure instead of work attributed to a
    /// User that cannot exist.
    /// </summary>
    public static Guid RequireUserId(this ClaimsPrincipal principal) =>
        principal.GetUserId()
        ?? throw new InvalidOperationException($"The request carries no usable '{UserId}' claim.");
}
