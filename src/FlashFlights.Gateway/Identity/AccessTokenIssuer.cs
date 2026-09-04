using FlashFlights.ServiceDefaults.Authentication;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace FlashFlights.Gateway.Identity;

/// <summary>A signed token and the moment it stops being accepted.</summary>
public sealed record AccessToken(string Token, DateTimeOffset ExpiresAt);

/// <summary>
/// Turns an authenticated User into the token every service will read them
/// back from. The gateway is the only issuer in the system — the services
/// validate, never mint — so this is the single seam where a claim enters
/// circulation.
/// </summary>
public interface IAccessTokenIssuer
{
    /// <param name="user">A User whose credentials have already been checked.</param>
    AccessToken Issue(FlashFlightsUser user);
}

/// <summary>
/// Signs with the same <see cref="JwtOptions"/> the services validate against,
/// resolved from configuration rather than passed in, so issuing and validating
/// cannot be pointed at different keys.
/// </summary>
public sealed class JwtAccessTokenIssuer(IOptions<JwtOptions> options, TimeProvider clock) : IAccessTokenIssuer
{
    private static readonly JsonWebTokenHandler Handler = new();

    public AccessToken Issue(FlashFlightsUser user)
    {
        // Email is how a User registers and how the SPA labels them, so a User
        // without one is a bug in whatever created it, not a token to sign.
        var email = user.Email
            ?? throw new InvalidOperationException($"User {user.Id} has no email to put in a token.");

        var jwt = options.Value;
        var issuedAt = clock.GetUtcNow();
        var expiresAt = issuedAt + jwt.Lifetime;

        var token = Handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = jwt.Issuer,
            Audience = jwt.Audience,
            IssuedAt = issuedAt.UtcDateTime,
            NotBefore = issuedAt.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            Claims = new Dictionary<string, object>
            {
                [FlashFlightsClaims.UserId] = user.Id.ToString(),
                [FlashFlightsClaims.Email] = email,
                // Distinct per token, so two tokens issued to one User in the
                // same second are still distinguishable in a log.
                [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString(),
            },
            SigningCredentials = new SigningCredentials(jwt.CreateSecurityKey(), SecurityAlgorithms.HmacSha256),
        });

        // Reported back to the client from the same arithmetic that signed the
        // token, rather than re-derived there from a lifetime it would have to
        // be told about separately.
        return new AccessToken(token, expiresAt);
    }
}

