using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FlashFlights.Ordering.Domain;
using FlashFlights.Ordering.Holds;
using FlashFlights.ServiceDefaults.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace FlashFlights.Ordering.Tests;

/// <summary>
/// The HTTP surface, driven over a real pipeline rather than by calling the
/// handler directly. Two things a service-level test cannot see: routing —
/// whether the Hold POST is reachable at the path the gateway forwards
/// (<c>/holds</c>, after it strips <c>/api/ordering</c>) — and how a decision the
/// service made reaches the client, which is where ticket 08 item 6's guarantee
/// is observed: a buyer who acted on a stale seat map is told cleanly, over the
/// wire, that Ordering refused, never shown a false success.
/// </summary>
public class HoldEndpointsTests
{
    /// <summary>
    /// The signing config the host validates against; a token minted from the
    /// same instance is accepted. One object, so issuing and validating in this
    /// test cannot drift the way the shipped system's shared key stops them
    /// drifting in production.
    /// </summary>
    private static readonly JwtOptions Signing = new()
    {
        Issuer = "flashflights-tests",
        Audience = "flashflights-tests-spa",
        SigningKey = "a-test-signing-key-well-past-the-hmac-sha256-minimum",
        Lifetime = TimeSpan.FromHours(2),
    };

    /// <summary>
    /// Item 6, at the edge a buyer actually meets: the live seat map is advisory,
    /// so a client can request a Seat it still shows Available a moment after
    /// someone else took it. Ordering is authoritative — its service tests prove
    /// it answers such a request with a <see cref="CreateHoldResult.Conflict"/>
    /// (never a grant) — and here we prove that answer reaches the buyer as a 409
    /// that names the blocking Seat, so the SPA can say which Seat to give up on
    /// rather than flashing a success that never happened.
    /// </summary>
    [Fact]
    public async Task A_hold_on_a_seat_someone_else_took_is_refused_with_the_seat_named()
    {
        var blocked = new ConflictingSeat(Guid.NewGuid(), "12C", nameof(SeatStatus.Held));
        await using var host = await StartAsync(new CreateHoldResult.Conflict([blocked]));

        var body = JsonSerializer.Serialize(new
        {
            flightId = Guid.NewGuid(),
            seatIds = new[] { blocked.SeatId },
            pricePerSeat = 49.99m,
        });
        var request = new HttpRequestMessage(HttpMethod.Post, HoldEndpoints.BasePath)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", MintToken(Guid.NewGuid()));

        var response = await host.Client.SendAsync(request);

        // Not a 201: the stale request is refused, not quietly granted.
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var namedSeat = problem.RootElement.GetProperty("seats")[0];
        Assert.Equal(blocked.SeatId, namedSeat.GetProperty("seatId").GetGuid());
        Assert.Equal("12C", namedSeat.GetProperty("seatNumber").GetString());
        Assert.Equal(nameof(SeatStatus.Held), namedSeat.GetProperty("status").GetString());
    }

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

    /// <summary>
    /// The manual sweep trigger is reachable at the forwarded path
    /// (<c>/holds/expire</c>) and, being a system action rather than an anonymous
    /// button, is behind auth: an unauthenticated post is a 401, not a 404.
    /// </summary>
    [Fact]
    public async Task Triggering_expiry_reaches_the_endpoint_at_the_forwarded_path()
    {
        await using var host = await StartAsync(new CreateHoldResult.Conflict([]));

        var response = await host.Client.PostAsJsonBody($"{HoldEndpoints.BasePath}/expire");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static async Task<HoldEndpointsHost> StartAsync(CreateHoldResult stubResult)
    {
        var builder = WebApplication.CreateSlimBuilder();

        // AddFlashFlightsJwtAuthentication validates its options on start, so the
        // host needs a signing config; the routing tests send no token, and the
        // conflict test mints one from the same Signing values.
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"{JwtOptions.SectionName}:Issuer"] = Signing.Issuer,
            [$"{JwtOptions.SectionName}:Audience"] = Signing.Audience,
            [$"{JwtOptions.SectionName}:SigningKey"] = Signing.SigningKey,
            [$"{JwtOptions.SectionName}:Lifetime"] = Signing.Lifetime.ToString(),
        });

        builder.AddFlashFlightsJwtAuthentication();
        builder.Services.AddSingleton<IHoldService>(new StubHoldService(stubResult));
        builder.WebHost.UseTestServer();

        var app = builder.Build();
        app.MapFlashFlightsHolds();
        await app.StartAsync();

        return new HoldEndpointsHost(app, app.GetTestClient());
    }

    /// <summary>
    /// A bearer token the host will accept, carrying the buyer's id in <c>sub</c>
    /// the way the gateway's issuer does — signed with the same <see cref="Signing"/>
    /// key the host validates against, so the request gets past auth and reaches
    /// the Hold handler.
    /// </summary>
    private static string MintToken(Guid userId) =>
        new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Signing.Issuer,
            Audience = Signing.Audience,
            Expires = DateTime.UtcNow.Add(Signing.Lifetime),
            Claims = new Dictionary<string, object>
            {
                [FlashFlightsClaims.UserId] = userId.ToString(),
                [FlashFlightsClaims.Email] = "buyer@flashflights.test",
            },
            SigningCredentials = new SigningCredentials(Signing.CreateSecurityKey(), SecurityAlgorithms.HmacSha256),
        });

    private sealed class StubHoldService(CreateHoldResult result) : IHoldService
    {
        public Task<CreateHoldResult> CreateHoldAsync(
            CreateHoldRequest request,
            CancellationToken cancellationToken = default) => Task.FromResult(result);

        // Routing is what these tests exercise; every post is rejected by auth
        // before the service is reached, so these never need a real answer.
        public Task<ConfirmHoldResult> ConfirmHoldAsync(
            Guid holdId,
            Guid userId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<ConfirmHoldResult>(new ConfirmHoldResult.NotFound());

        public Task<ExpireHoldsResult> ExpireHoldsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new ExpireHoldsResult(0, 0));
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
