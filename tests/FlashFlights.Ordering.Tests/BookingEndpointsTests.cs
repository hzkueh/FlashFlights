using System.Net;
using FlashFlights.Ordering.Bookings;
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
/// The booking-history HTTP surface over a real pipeline. The one thing a
/// service-level test cannot see is routing and auth: whether the list is
/// reachable at the path the gateway forwards (<c>/bookings</c>, after it strips
/// <c>/api/ordering</c>) and, being one buyer's private purchases, sits behind
/// auth. An anonymous GET must be rejected by auth (401), not lost by routing
/// (404) nor served to no-one in particular.
/// </summary>
public class BookingEndpointsTests
{
    [Fact]
    public async Task Listing_bookings_reaches_the_endpoint_at_the_forwarded_path_behind_auth()
    {
        await using var host = await StartAsync();

        var response = await host.Client.GetAsync(BookingEndpoints.BasePath);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static async Task<BookingEndpointsHost> StartAsync()
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
        builder.Services.AddSingleton<IBookingReadService>(new StubBookingReadService());
        builder.WebHost.UseTestServer();

        var app = builder.Build();
        app.MapFlashFlightsBookings();
        await app.StartAsync();

        return new BookingEndpointsHost(app, app.GetTestClient());
    }

    private sealed class StubBookingReadService : IBookingReadService
    {
        // Routing and auth are what this test exercises; the GET is rejected by
        // auth before the service is reached, so it never needs a real answer.
        public Task<IReadOnlyList<BookingView>> ListBookingsForUserAsync(
            Guid userId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<BookingView>>([]);
    }

    private sealed class BookingEndpointsHost(WebApplication app, HttpClient client) : IAsyncDisposable
    {
        public HttpClient Client { get; } = client;

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await app.DisposeAsync();
        }
    }
}
