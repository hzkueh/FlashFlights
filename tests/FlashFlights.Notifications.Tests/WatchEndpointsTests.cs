using System.Net;
using System.Net.Http.Headers;
using FlashFlights.Notifications.Watching;
using FlashFlights.ServiceDefaults.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace FlashFlights.Notifications.Tests;

/// <summary>
/// The Watch and inbox HTTP surface over a real pipeline. What the service-level
/// suites cannot see is everything between the token and the service call:
/// whether each route is reachable at the path the gateway forwards (the gateway
/// strips <c>/api/notifications</c>), whether it is behind auth at all, and
/// whether a service outcome becomes the status code the SPA is written against
/// — a refused Watch as 409 and someone else's Notification as 404 rather than
/// both collapsing to 500.
///
/// The services themselves are stubs here on purpose: their behaviour is proven
/// in <see cref="WatchServiceTests"/> and <see cref="NotificationInboxTests"/>,
/// and repeating it against a database would only make this suite slower at
/// answering a different question.
/// </summary>
public class WatchEndpointsTests
{
    private static readonly Guid Bearer = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid FlightId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Theory]
    [InlineData("GET", WatchEndpoints.WatchesPath)]
    [InlineData("PUT", $"{WatchEndpoints.WatchesPath}/22222222-2222-2222-2222-222222222222")]
    [InlineData("DELETE", $"{WatchEndpoints.WatchesPath}/22222222-2222-2222-2222-222222222222")]
    [InlineData("GET", WatchEndpoints.InboxPath)]
    [InlineData("POST", $"{WatchEndpoints.InboxPath}/33333333-3333-3333-3333-333333333333/read")]
    public async Task Every_route_is_reachable_at_the_forwarded_path_and_behind_auth(string method, string path)
    {
        await using var host = await StartAsync();

        var response = await host.Client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path));

        // 401 and not 404: rejected by auth, rather than lost by routing — which
        // would look like the same "no" to a client and be a very different bug.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Watching_an_upcoming_sale_answers_no_content()
    {
        var watches = new StubWatchService();
        await using var host = await StartAsync(watches);

        var response = await host.SignedIn().PutAsync($"{WatchEndpoints.WatchesPath}/{FlightId}", content: null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Watching_a_sale_that_has_already_opened_answers_conflict()
    {
        var watches = new StubWatchService { Outcome = WatchOutcome.SaleAlreadyStarted };
        await using var host = await StartAsync(watches);

        var response = await host.SignedIn().PutAsync($"{WatchEndpoints.WatchesPath}/{FlightId}", content: null);

        // The one outcome the SPA has to tell apart from success: the toggle
        // shows it rather than silently leaving itself switched on.
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Un_watching_answers_no_content()
    {
        var watches = new StubWatchService();
        await using var host = await StartAsync(watches);

        var response = await host.SignedIn().DeleteAsync($"{WatchEndpoints.WatchesPath}/{FlightId}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal((Bearer, FlightId), watches.Unwatched!.Value);
    }

    [Fact]
    public async Task Marking_a_notification_that_is_not_this_users_answers_not_found()
    {
        var inbox = new StubNotificationInbox { Marked = false };
        await using var host = await StartAsync(inbox: inbox);

        var response = await host.SignedIn()
            .PostAsync($"{WatchEndpoints.InboxPath}/{Guid.NewGuid()}/read", content: null);

        // 404 rather than 403: the inbox is scoped to the token, so from out
        // here someone else's alert simply does not exist.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Marking_this_users_notification_answers_no_content()
    {
        var inbox = new StubNotificationInbox { Marked = true };
        await using var host = await StartAsync(inbox: inbox);

        var response = await host.SignedIn()
            .PostAsync($"{WatchEndpoints.InboxPath}/{Guid.NewGuid()}/read", content: null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Every_route_is_scoped_to_the_token_rather_than_anything_the_caller_sends()
    {
        var watches = new StubWatchService();
        var inbox = new StubNotificationInbox();
        await using var host = await StartAsync(watches, inbox);
        var client = host.SignedIn();

        await client.GetAsync(WatchEndpoints.WatchesPath);
        await client.PutAsync($"{WatchEndpoints.WatchesPath}/{FlightId}", content: null);
        await client.DeleteAsync($"{WatchEndpoints.WatchesPath}/{FlightId}");
        await client.GetAsync(WatchEndpoints.InboxPath);
        await client.PostAsync($"{WatchEndpoints.InboxPath}/{Guid.NewGuid()}/read", content: null);

        // There is deliberately no way to name a User from outside, so every
        // call has to have reached the service as the bearer and nobody else.
        Assert.All(watches.Callers.Concat(inbox.Callers), caller => Assert.Equal(Bearer, caller));
        Assert.Equal(3, watches.Callers.Count);
        Assert.Equal(2, inbox.Callers.Count);
    }

    private static readonly JwtOptions SigningConfig = new()
    {
        Issuer = "flashflights-tests",
        Audience = "flashflights-tests-spa",
        SigningKey = "a-test-signing-key-well-past-the-hmac-sha256-minimum",
        Lifetime = TimeSpan.FromHours(2),
    };

    private static async Task<WatchEndpointsHost> StartAsync(
        StubWatchService? watches = null,
        StubNotificationInbox? inbox = null)
    {
        var builder = WebApplication.CreateSlimBuilder();

        // AddFlashFlightsJwtAuthentication validates its options on start, so
        // the host needs the signing config even for the anonymous cases.
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"{JwtOptions.SectionName}:Issuer"] = SigningConfig.Issuer,
            [$"{JwtOptions.SectionName}:Audience"] = SigningConfig.Audience,
            [$"{JwtOptions.SectionName}:SigningKey"] = SigningConfig.SigningKey,
            [$"{JwtOptions.SectionName}:Lifetime"] = SigningConfig.Lifetime.ToString(),
        });

        builder.AddFlashFlightsJwtAuthentication();
        builder.Services.AddSingleton<IWatchService>(watches ?? new StubWatchService());
        builder.Services.AddSingleton<INotificationInbox>(inbox ?? new StubNotificationInbox());
        builder.WebHost.UseTestServer();

        var app = builder.Build();
        app.MapFlashFlightsWatches();
        await app.StartAsync();

        return new WatchEndpointsHost(app, app.GetTestClient());
    }

    /// <summary>A token as the gateway would have signed it, naming the bearer.</summary>
    private static string TokenForBearer() =>
        new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = SigningConfig.Issuer,
            Audience = SigningConfig.Audience,
            Expires = DateTime.UtcNow.Add(SigningConfig.Lifetime),
            Claims = new Dictionary<string, object>
            {
                [FlashFlightsClaims.UserId] = Bearer.ToString(),
                [FlashFlightsClaims.Email] = "watcher@flashflights.test",
            },
            SigningCredentials = new SigningCredentials(
                SigningConfig.CreateSecurityKey(),
                SecurityAlgorithms.HmacSha256),
        });

    private sealed class StubWatchService : IWatchService
    {
        public WatchOutcome Outcome { get; init; } = WatchOutcome.Watched;

        public List<Guid> Callers { get; } = [];

        public (Guid UserId, Guid FlightId)? Unwatched { get; private set; }

        public Task<WatchOutcome> WatchAsync(Guid userId, Guid flightId, CancellationToken cancellationToken = default)
        {
            Callers.Add(userId);
            return Task.FromResult(Outcome);
        }

        public Task UnwatchAsync(Guid userId, Guid flightId, CancellationToken cancellationToken = default)
        {
            Callers.Add(userId);
            Unwatched = (userId, flightId);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<Guid>> ListWatchedFlightIdsAsync(
            Guid userId,
            CancellationToken cancellationToken = default)
        {
            Callers.Add(userId);
            return Task.FromResult<IReadOnlyList<Guid>>([]);
        }
    }

    private sealed class StubNotificationInbox : INotificationInbox
    {
        public bool Marked { get; init; } = true;

        public List<Guid> Callers { get; } = [];

        public Task<InboxView> ListAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            Callers.Add(userId);
            return Task.FromResult(new InboxView([], 0));
        }

        public Task<bool> MarkReadAsync(
            Guid userId,
            Guid notificationId,
            CancellationToken cancellationToken = default)
        {
            Callers.Add(userId);
            return Task.FromResult(Marked);
        }
    }

    private sealed class WatchEndpointsHost(WebApplication app, HttpClient client) : IAsyncDisposable
    {
        public HttpClient Client { get; } = client;

        /// <summary>The same client, carrying a token for <see cref="Bearer"/>.</summary>
        public HttpClient SignedIn()
        {
            Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenForBearer());
            return Client;
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await app.DisposeAsync();
        }
    }
}
