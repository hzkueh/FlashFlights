using System.Net;
using System.Net.Http.Headers;
using FlashFlights.ServiceDefaults;
using FlashFlights.ServiceDefaults.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace FlashFlights.Gateway.Tests;

/// <summary>
/// The other half of the arrangement that lets auth be a lightweight piece
/// alongside the gateway rather than a fourth microservice: a service holds no
/// user store and cannot issue anything, so all it does is accept or reject a
/// token against the shared signing config.
///
/// Driven over HTTP against a host wired the way a service is, because whether
/// a request is refused is decided by middleware — a handler called directly
/// would answer happily with no token at all.
/// </summary>
public class SharedTokenValidationTests
{
    [Fact]
    public async Task A_protected_endpoint_refuses_a_caller_with_no_token()
    {
        await using var service = await AuthTestHost.StartServiceAsync();

        var response = await service.Client.GetAsync(AuthTestHost.ProtectedPath);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_protected_endpoint_accepts_a_token_the_gateway_signed()
    {
        var signingConfig = AuthTestHost.NewSigningConfig();
        await using var service = await AuthTestHost.StartServiceAsync(signingConfig);

        var response = await GetProtectedAsync(service, AuthTestHost.IssueToken(signingConfig).Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// The signing key is the whole trust boundary: a service that accepted a
    /// token signed with any other key would accept a token anyone could mint.
    /// </summary>
    [Fact]
    public async Task A_token_signed_with_another_key_is_refused()
    {
        await using var service = await AuthTestHost.StartServiceAsync();
        var elsewhere = AuthTestHost.NewSigningConfig();
        elsewhere.SigningKey = "a-different-key-that-is-also-long-enough-for-hmac-sha256";

        var response = await GetProtectedAsync(service, AuthTestHost.IssueToken(elsewhere).Token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_token_from_another_issuer_is_refused()
    {
        await using var service = await AuthTestHost.StartServiceAsync();
        var elsewhere = AuthTestHost.NewSigningConfig();
        elsewhere.Issuer = "someone-elses-gateway";

        var response = await GetProtectedAsync(service, AuthTestHost.IssueToken(elsewhere).Token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// Bearer validation forgives five minutes of clock skew by default. The
    /// same system issues and validates these tokens, so that default would only
    /// mean an expired token keeps working — this asserts it does not.
    /// </summary>
    [Fact]
    public async Task A_token_that_has_expired_is_refused_without_grace()
    {
        var signingConfig = AuthTestHost.NewSigningConfig();
        await using var service = await AuthTestHost.StartServiceAsync(signingConfig);

        var expired = AuthTestHost.IssueToken(
            signingConfig,
            issuedAt: DateTimeOffset.UtcNow - signingConfig.Lifetime - TimeSpan.FromSeconds(30));

        var response = await GetProtectedAsync(service, expired.Token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// Catalog, Ordering, and Notifications all get validation by taking the
    /// service defaults, so none of them can be left with endpoints it believes
    /// are protected and a host that never checks a token. Asserted on the
    /// registration rather than over HTTP: standing up any one of those services
    /// would drag in its datastore and the bus, neither of which decides this.
    /// </summary>
    [Fact]
    public async Task Every_service_that_takes_the_service_defaults_validates_bearer_tokens()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration[$"{JwtOptions.SectionName}:SigningKey"] = AuthTestHost.NewSigningConfig().SigningKey;

        builder.AddFlashFlightsServiceDefaults("service-under-test");
        using var host = builder.Build();

        var schemes = host.Services.GetRequiredService<IAuthenticationSchemeProvider>();

        Assert.NotNull(await schemes.GetSchemeAsync(JwtBearerDefaults.AuthenticationScheme));
    }

    /// <summary>
    /// A host with no signing key configured would start and then reject every
    /// token it was ever sent, which looks like a client bug from every side.
    /// It refuses to start instead.
    /// </summary>
    [Fact]
    public async Task A_host_with_no_signing_key_configured_refuses_to_start()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.AddFlashFlightsJwtAuthentication();
        using var host = builder.Build();

        var failure = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());

        Assert.Contains(nameof(JwtOptions.SigningKey), string.Join(' ', failure.Failures));
    }

    private static Task<HttpResponseMessage> GetProtectedAsync(AuthTestServer service, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, AuthTestHost.ProtectedPath);
        request.Headers.Authorization = new AuthenticationHeaderValue(JwtBearerDefaults.AuthenticationScheme, token);

        return service.Client.SendAsync(request);
    }
}
