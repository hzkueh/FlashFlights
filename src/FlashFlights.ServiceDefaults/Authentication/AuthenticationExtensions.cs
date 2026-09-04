using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace FlashFlights.ServiceDefaults.Authentication;

/// <summary>
/// The one place a FlashFlights host learns how to validate a token. Called by
/// <see cref="ServiceDefaultsExtensions.AddFlashFlightsServiceDefaults"/>, so
/// Catalog, Ordering, and Notifications cannot take the service defaults and
/// still be missing validation; the gateway calls it directly, because it takes
/// no part in the bus.
/// </summary>
public static class AuthenticationExtensions
{
    /// <summary>
    /// Binds <see cref="JwtOptions"/> and registers bearer validation against
    /// it. Nothing is protected by this call alone — an endpoint opts in with
    /// <c>RequireAuthorization</c>, which is what keeps browsing the catalog and
    /// seat maps available unauthenticated.
    ///
    /// No <c>UseAuthentication</c>/<c>UseAuthorization</c> call belongs with it:
    /// <c>WebApplication</c> inserts both itself once these services are
    /// registered. Left to it deliberately — a per-host pair of calls is a
    /// per-host pair of calls to forget, which would leave a service with
    /// endpoints it believes are protected and a pipeline that never reads a
    /// token. The tests' service host maps a protected endpoint and adds no
    /// middleware of its own, so that this stays true.
    /// </summary>
    public static IHostApplicationBuilder AddFlashFlightsJwtAuthentication(this IHostApplicationBuilder builder)
    {
        builder.Services.AddOptions<JwtOptions>()
            .Bind(builder.Configuration.GetSection(JwtOptions.SectionName))
            .ValidateDataAnnotations()
            // At startup: a host with no signing key configured refuses to
            // start, rather than starting and rejecting every token it is sent.
            .ValidateOnStart();

        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer();

        builder.Services.AddSingleton<IConfigureOptions<JwtBearerOptions>, ConfigureJwtBearerFromJwtOptions>();
        builder.Services.AddAuthorization();

        return builder;
    }

    /// <summary>
    /// Derives the bearer scheme's validation rules from <see cref="JwtOptions"/>
    /// rather than from its own configuration section, so signing config is
    /// stated once and the issuing and validating halves cannot drift apart.
    /// </summary>
    private sealed class ConfigureJwtBearerFromJwtOptions(IOptions<JwtOptions> jwtOptions)
        : IConfigureNamedOptions<JwtBearerOptions>
    {
        public void Configure(JwtBearerOptions options) => Configure(Options.DefaultName, options);

        public void Configure(string? name, JwtBearerOptions options)
        {
            if (name != JwtBearerDefaults.AuthenticationScheme)
            {
                return;
            }

            var jwt = jwtOptions.Value;

            // Claims arrive as they were written: `sub` stays `sub` instead of
            // being rewritten to a ClaimTypes URI, so FlashFlightsClaims names
            // one thing on both the issuing and the reading side.
            options.MapInboundClaims = false;

            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = jwt.Issuer,
                ValidateAudience = true,
                ValidAudience = jwt.Audience,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = jwt.CreateSecurityKey(),
                ValidateLifetime = true,
                // No skew to forgive: the same system issues and validates
                // these, so the default five minutes would only mean an expired
                // token keeps working for five more.
                ClockSkew = TimeSpan.Zero,
                NameClaimType = FlashFlightsClaims.Email,
            };
        }
    }
}
