using System.Security.Claims;
using FlashFlights.ServiceDefaults.Authentication;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;

namespace FlashFlights.Gateway.Identity;

/// <summary>
/// Register, log in, and "who am I" — answered by the gateway itself rather
/// than proxied, because the user store lives here. Thin by design: each
/// handler validates nothing Identity already validates and issues nothing
/// <see cref="IAccessTokenIssuer"/> does not.
/// </summary>
public static class AuthEndpoints
{
    /// <summary>
    /// Under <c>/api</c> like every other backend call, so the SPA's one
    /// root-relative view of the backend still holds and nothing has to know
    /// that this half is not behind the proxy.
    /// </summary>
    public const string BasePath = "/api/auth";

    /// <summary>
    /// Deliberately does not say which half was wrong: telling a caller that
    /// the email exists but the password is wrong turns this endpoint into a
    /// way to enumerate registered emails.
    /// </summary>
    public const string IncorrectCredentials = "That email and password do not match.";

    public static IEndpointRouteBuilder MapFlashFlightsAuth(this IEndpointRouteBuilder endpoints)
    {
        var auth = endpoints.MapGroup(BasePath);

        auth.MapPost("/register", RegisterAsync);
        auth.MapPost("/login", LogInAsync);

        // The only protected endpoint the gateway hosts: how a client with a
        // stored token finds out whether it is still one the system accepts.
        auth.MapGet("/me", Me).RequireAuthorization();

        return endpoints;
    }

    private static async Task<IResult> RegisterAsync(
        CredentialsRequest request,
        UserManager<FlashFlightsUser> users,
        IAccessTokenIssuer issuer)
    {
        var email = request.Email?.Trim() ?? string.Empty;

        // Version 7 rather than v4: time-ordered, so the primary key a growing
        // user table is clustered on stays append-mostly.
        var user = new FlashFlightsUser { Id = Guid.CreateVersion7(), UserName = email, Email = email };

        var result = await users.CreateAsync(user, request.Password ?? string.Empty);

        if (!result.Succeeded)
        {
            return TypedResults.ValidationProblem(IdentityErrorReport.ToValidationErrors(result.Errors));
        }

        // Signed in immediately: making someone register and then log in with
        // the credentials they just typed is a step with no purpose.
        return TypedResults.Ok(SessionFor(user, issuer));
    }

    private static async Task<IResult> LogInAsync(
        CredentialsRequest request,
        UserManager<FlashFlightsUser> users,
        IAccessTokenIssuer issuer)
    {
        var user = await users.FindByEmailAsync(request.Email?.Trim() ?? string.Empty);

        // An unknown email skips the hash comparison and so answers faster than
        // a wrong password does. Closing that timing gap needs a dummy
        // verification, which is worth doing the day this stops being a
        // portfolio demo with seeded data.
        if (user is null || !await users.CheckPasswordAsync(user, request.Password ?? string.Empty))
        {
            return TypedResults.Problem(
                title: "Sign in failed",
                detail: IncorrectCredentials,
                statusCode: StatusCodes.Status401Unauthorized);
        }

        return TypedResults.Ok(SessionFor(user, issuer));
    }

    /// <summary>
    /// Reads back exactly the claims <see cref="IAccessTokenIssuer"/> wrote —
    /// no store lookup, because a validated token already carries the answer.
    /// </summary>
    private static Ok<SignedInUser> Me(ClaimsPrincipal principal) =>
        TypedResults.Ok(new SignedInUser(
            principal.RequireUserId(),
            principal.FindFirstValue(FlashFlightsClaims.Email) ?? string.Empty));

    private static SessionResponse SessionFor(FlashFlightsUser user, IAccessTokenIssuer issuer)
    {
        var token = issuer.Issue(user);

        return new SessionResponse(token.Token, token.ExpiresAt, user.Id, user.Email!);
    }
}
