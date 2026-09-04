using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FlashFlights.Gateway.Identity;

/// <summary>
/// The one shared user store and the token issuance that goes with it, wired as
/// a lightweight piece alongside the gateway rather than a fourth microservice.
/// Registered as an extension so the gateway's <c>Program</c> stays a thin
/// caller and the same wiring can be stood up in a test.
/// </summary>
public static class IdentityExtensions
{
    /// <summary>
    /// Identity plus the issuer. The store itself is registered separately with
    /// <c>AddFlashFlightsDataStore</c>, because only the host knows where its
    /// database lives, and validating tokens is separate again — signing and
    /// checking a signature are two capabilities, and the gateway happens to
    /// have both.
    /// </summary>
    public static IHostApplicationBuilder AddFlashFlightsIdentity(this IHostApplicationBuilder builder)
    {
        // Core, not AddIdentity: there are no cookies, no external providers and
        // no roles here — a request arrives with a bearer token or arrives
        // unauthenticated.
        builder.Services.AddIdentityCore<FlashFlightsUser>(options =>
            {
                // Email is the identity, so two Users may not share one.
                options.User.RequireUniqueEmail = true;

                // Longer than Identity's default six, and without the symbol
                // requirement: length is the part that actually costs an
                // attacker, and the symbol rule mostly costs a reviewer trying
                // to sign in during a demo.
                options.Password.RequiredLength = 8;
                options.Password.RequireNonAlphanumeric = false;
            })
            .AddEntityFrameworkStores<FlashFlightsIdentityDbContext>();

        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<IAccessTokenIssuer, JwtAccessTokenIssuer>();

        return builder;
    }
}
