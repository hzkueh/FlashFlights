using System.Net;
using FlashFlights.Ordering.Holds;
using FlashFlights.ServiceDefaults.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace FlashFlights.Ordering.Tests;

/// <summary>
/// The HTTP surface, driven over a real pipeline rather than by calling the
/// handler directly. The one thing service-level tests cannot see is routing:
/// whether the Hold POST is actually reachable at the path the gateway forwards
/// (<c>/holds</c>, after it strips <c>/api/ordering</c>). "Reachable over HTTP"
/// is an acceptance criterion, so it gets a test that would fail if the route
/// template drifted to <c>/holds/</c>.
/// </summary>
public class HoldEndpointsTests
{
    /// <summary>
    /// An unauthenticated POST to the exact path the gateway forwards must be
    /// rejected by auth (401), not lost by routing (404). A 404 here is the
    /// trailing-slash trap: the endpoint registered at /holds/ and the forwarded
    /// /holds missing it.
    /// </summary>
    [Fact]
    public async Task Posting_a_hold_reaches_the_endpoint_at_the_forwarded_path()
    {
        await using var host = await StartAsync(new CreateHoldResult.Conflict([]));

        var response = await host.Client.PostAsJsonBody(HoldEndpoints.BasePath);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// The confirm route is reachable at the path the gateway forwards
    /// (<c>/holds/{id}/confirm</c>): an unauthenticated post is rejected by auth
    /// (401), not lost by routing (404). Guards the route template — a drift in
    /// the id constraint or the segment would surface here as a 404.
    /// </summary>
    [Fact]
    public async Task Confirming_a_hold_reaches_the_endpoint_at_the_forwarded_path()
    {
        await using var host = await StartAsync(new CreateHoldResult.Conflict([]));

        var response = await host.Client.PostAsJsonBody($"{HoldEndpoints.BasePath}/{Guid.NewGuid()}/confirm");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static async Task<HoldEndpointsHost> StartAsync(CreateHoldResult stubResult)
    {
        var builder = WebApplication.CreateSlimBuilder();

        // AddFlashFlightsJwtAuthentication validates its options on start, so the
        // host needs a signing config even though this test sends no token.
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"{JwtOptions.SectionName}:Issuer"] = "flashflights-tests",
            [$"{JwtOptions.SectionName}:Audience"] = "flashflights-tests-spa",
            [$"{JwtOptions.SectionName}:SigningKey"] = "a-test-signing-key-well-past-the-hmac-sha256-minimum",
            [$"{JwtOptions.SectionName}:Lifetime"] = TimeSpan.FromHours(2).ToString(),
        });

        builder.AddFlashFlightsJwtAuthentication();
        builder.Services.AddSingleton<IHoldService>(new StubHoldService(stubResult));
        builder.WebHost.UseTestServer();

        var app = builder.Build();
        app.MapFlashFlightsHolds();
        await app.StartAsync();

        return new HoldEndpointsHost(app, app.GetTestClient());
    }

    private sealed class StubHoldService(CreateHoldResult result) : IHoldService
    {
        public Task<CreateHoldResult> CreateHoldAsync(
            CreateHoldRequest request,
            CancellationToken cancellationToken = default) => Task.FromResult(result);

        // Routing is what these tests exercise; both posts are rejected by auth
        // before the service is reached, so confirm never needs a real answer.
        public Task<ConfirmHoldResult> ConfirmHoldAsync(
            Guid holdId,
            Guid userId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<ConfirmHoldResult>(new ConfirmHoldResult.NotFound());
    }

    private sealed class HoldEndpointsHost(WebApplication app, HttpClient client) : IAsyncDisposable
    {
        public HttpClient Client { get; } = client;

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await app.DisposeAsync();
        }
    }
}

internal static class HttpClientJsonExtensions
{
    /// <summary>An empty JSON body is enough: the route match is decided before the body is read.</summary>
    public static Task<HttpResponseMessage> PostAsJsonBody(this HttpClient client, string path) =>
        client.PostAsync(path, new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));
}
