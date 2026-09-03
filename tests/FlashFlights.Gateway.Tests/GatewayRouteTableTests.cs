using Microsoft.Extensions.Configuration;
using Yarp.ReverseProxy.Configuration;

namespace FlashFlights.Gateway.Tests;

/// <summary>
/// The gateway is the system's single entry point: the SPA is behind it at
/// <c>/</c> and the services behind <c>/api/*</c>. That arrangement lives
/// entirely in configuration, so it is asserted against the file the gateway
/// actually ships rather than a hand-built one.
/// </summary>
public class GatewayRouteTableTests
{
    private static readonly IConfiguration Configuration = new ConfigurationBuilder()
        .AddJsonFile("appsettings.json", optional: false)
        .Build();

    private static readonly IReadOnlyList<RouteConfig> Routes =
        Configuration.GetSection("ReverseProxy:Routes").Get<Dictionary<string, RouteConfig>>()!
            .Select(entry => entry.Value with { RouteId = entry.Key })
            .ToList();

    [Theory]
    [InlineData("catalog")]
    [InlineData("ordering")]
    [InlineData("notifications")]
    public void Every_service_is_reachable_under_its_api_prefix(string service)
    {
        var route = Routes.Single(route => route.RouteId == service);

        Assert.Equal($"/api/{service}/{{**catch-all}}", route.Match.Path);
        Assert.Equal(service, route.ClusterId);
        Assert.NotNull(Configuration[$"ReverseProxy:Clusters:{service}:Destinations:primary:Address"]);
    }

    /// <summary>Anything that is not an API call belongs to the SPA's own router.</summary>
    [Fact]
    public void Spa_owns_every_path_that_is_not_an_api_call()
    {
        var web = Routes.Single(route => route.RouteId == "web");

        Assert.Equal("/{**catch-all}", web.Match.Path);
        Assert.Equal("web", web.ClusterId);
        Assert.NotNull(Configuration["ReverseProxy:Clusters:web:Destinations:primary:Address"]);
    }

    /// <summary>
    /// The SPA's catch-all would swallow the API prefixes if it were considered
    /// first. YARP would prefer the more specific match anyway; the explicit
    /// order is what stops a future route from quietly depending on that.
    /// </summary>
    [Fact]
    public void Spa_route_is_considered_after_every_api_route()
    {
        var web = Routes.Single(route => route.RouteId == "web");

        Assert.All(
            Routes.Where(route => route.RouteId != "web"),
            route => Assert.True((route.Order ?? 0) < web.Order));
    }

    /// <summary>
    /// The SPA is same-origin with the gateway, so its route needs no CORS
    /// policy — the named policy exists for a client on some other origin.
    /// </summary>
    [Fact]
    public void Spa_route_needs_no_cors_policy()
    {
        Assert.Null(Routes.Single(route => route.RouteId == "web").CorsPolicy);
    }
}
