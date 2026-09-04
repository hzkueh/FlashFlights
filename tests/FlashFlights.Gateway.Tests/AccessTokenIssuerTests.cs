using FlashFlights.Gateway.Identity;
using FlashFlights.ServiceDefaults.Authentication;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;

namespace FlashFlights.Gateway.Tests;

/// <summary>
/// What a FlashFlights token actually says. The gateway is the system's only
/// issuer, so every service reads a User's identity out of exactly these
/// claims — getting a name wrong here would be a system-wide 401, not a local
/// bug.
///
/// These read the token without validating it: the rules for accepting one are
/// <see cref="SharedTokenValidationTests"/>'s job.
/// </summary>
public class AccessTokenIssuerTests
{
    private static readonly DateTimeOffset IssuedAt = new(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);

    private static readonly JwtOptions SigningConfig = AuthTestHost.NewSigningConfig();

    [Fact]
    public void Token_names_the_user_it_was_issued_to()
    {
        var user = UserWith("buyer@flashflights.test");

        var payload = Payload(Issue(user));

        Assert.Equal(user.Id.ToString(), payload.GetPayloadValue<string>(FlashFlightsClaims.UserId));
    }

    /// <summary>
    /// So a client holding a token can label the session without a second call
    /// asking who it is.
    /// </summary>
    [Fact]
    public void Token_carries_the_email_the_session_is_labelled_with()
    {
        var payload = Payload(Issue(UserWith("buyer@flashflights.test")));

        Assert.Equal("buyer@flashflights.test", payload.GetPayloadValue<string>(FlashFlightsClaims.Email));
    }

    /// <summary>
    /// The reported expiry has to be the token's own, not a second guess the
    /// client derives from a lifetime it was told about separately.
    /// </summary>
    [Fact]
    public void Reported_expiry_is_the_expiry_the_token_was_signed_with()
    {
        var issued = Issue(UserWith("buyer@flashflights.test"));

        Assert.Equal(IssuedAt + SigningConfig.Lifetime, issued.ExpiresAt);
        Assert.Equal(issued.ExpiresAt.UtcDateTime, Payload(issued).ValidTo, TimeSpan.FromSeconds(1));
    }

    /// <summary>Two tokens for one User in the same second are still distinguishable in a log.</summary>
    [Fact]
    public void Every_token_is_individually_identified()
    {
        var user = UserWith("buyer@flashflights.test");

        Assert.NotEqual(
            Payload(Issue(user)).GetPayloadValue<string>(JwtRegisteredClaimNames.Jti),
            Payload(Issue(user)).GetPayloadValue<string>(JwtRegisteredClaimNames.Jti));
    }

    /// <summary>
    /// Email is how a User registers and how the SPA labels them, so signing a
    /// token without one would hand every service a session it cannot render.
    /// </summary>
    [Fact]
    public void A_user_without_an_email_cannot_be_issued_a_token()
    {
        var user = new FlashFlightsUser { Id = Guid.CreateVersion7(), UserName = "no-email" };

        var failure = Assert.Throws<InvalidOperationException>(() => Issue(user));

        Assert.Contains(user.Id.ToString(), failure.Message);
    }

    private static FlashFlightsUser UserWith(string email) =>
        new() { Id = Guid.CreateVersion7(), UserName = email, Email = email };

    private static AccessToken Issue(FlashFlightsUser user) =>
        new JwtAccessTokenIssuer(Options.Create(SigningConfig), new FixedClock(IssuedAt)).Issue(user);

    private static JsonWebToken Payload(AccessToken issued) => new(issued.Token);
}
