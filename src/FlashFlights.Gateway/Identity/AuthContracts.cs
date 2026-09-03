namespace FlashFlights.Gateway.Identity;

/// <summary>
/// What both auth screens post — registering and signing in ask for the same
/// pair, and the SPA models them as one thing too.
///
/// Email and password are nullable because a client can post anything: an
/// absent field becomes an empty string that Identity's own validators reject
/// and report, rather than a 400 from model binding with nothing useful in it.
/// </summary>
public sealed record CredentialsRequest(string? Email, string? Password);

/// <summary>
/// What register and login both answer with. Flat rather than nesting the User,
/// because the client stores it as one unit and every field is something it
/// needs: the token to send, the expiry to know when to stop, and the identity
/// to render.
/// </summary>
public sealed record SessionResponse(string Token, DateTimeOffset ExpiresAt, Guid UserId, string Email);

/// <summary>Who the bearer of a token is, per the gateway that issued it.</summary>
public sealed record SignedInUser(Guid UserId, string Email);
