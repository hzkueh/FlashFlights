using System.Net;
using System.Net.Http.Json;
using FlashFlights.DemoData;
using FlashFlights.Gateway.Identity;
using FlashFlights.Gateway.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FlashFlights.Gateway.Tests;

/// <summary>
/// The gateway's half of ticket 11: a reviewer can sign in as a demo buyer and
/// find the booking history and inbox the other services seeded, and a restart
/// leaves those accounts alone.
///
/// <para>
/// Sign-in is asserted over HTTP against the real <c>/api/auth/login</c>, because
/// the only interesting question about a seeded account is whether it can
/// actually be used — a row that exists but will not authenticate is worse than
/// no row at all.
/// </para>
/// </summary>
public class IdentityDemoSeederTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 17, 9, 0, 0, TimeSpan.Zero);

    private static DemoWorld World() => DemoWorld.Create(Now, new DemoSeedOptions());

    [Fact]
    public async Task Seeds_every_demo_buyer()
    {
        await using var gateway = await AuthTestHost.StartGatewayAsync();

        Assert.True(await SeedAsync(gateway));

        await using var scope = gateway.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FlashFlightsIdentityDbContext>();

        Assert.Equal(
            World().Users.Select(user => user.Email).Order(),
            (await db.Users.Select(user => user.Email).ToListAsync()).Order());
    }

    [Fact]
    public async Task A_demo_buyer_can_sign_in_with_the_published_password()
    {
        await using var gateway = await AuthTestHost.StartGatewayAsync();
        await SeedAsync(gateway);

        var response = await gateway.Client.PostAsJsonAsync(
            $"{AuthEndpoints.BasePath}/login",
            new CredentialsRequest(DemoUsers.Ada.Email, DemoUsers.Password));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(DemoUsers.Ada.Email, (await response.Content.ReadFromJsonAsync<SessionResponse>())!.Email);
    }

    /// <summary>
    /// The id every other service has already written Holds, Watches, and
    /// Notifications against. Nothing joins across those stores, so a different
    /// id here would not fail anywhere — it would simply mean a buyer signs in to
    /// an empty account, which is exactly the state this ticket exists to avoid.
    /// </summary>
    [Fact]
    public async Task A_demo_buyer_signs_in_as_the_user_the_other_services_seeded_against()
    {
        await using var gateway = await AuthTestHost.StartGatewayAsync();
        await SeedAsync(gateway);

        var response = await gateway.Client.PostAsJsonAsync(
            $"{AuthEndpoints.BasePath}/login",
            new CredentialsRequest(DemoUsers.Ada.Email, DemoUsers.Password));

        var session = await response.Content.ReadFromJsonAsync<SessionResponse>();

        Assert.Equal(DemoUsers.Ada.Id, session!.UserId);
    }

    [Fact]
    public async Task A_second_run_over_a_seeded_store_creates_nobody()
    {
        await using var gateway = await AuthTestHost.StartGatewayAsync();
        await SeedAsync(gateway);

        Assert.False(await SeedAsync(gateway));

        await using var scope = gateway.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FlashFlightsIdentityDbContext>();

        Assert.Equal(World().Users.Count, await db.Users.CountAsync());
    }

    /// <summary>
    /// A store someone has already registered against is not one to quietly add
    /// accounts with a published password to.
    /// </summary>
    [Fact]
    public async Task Leaves_a_store_that_already_has_a_registered_user_alone()
    {
        await using var gateway = await AuthTestHost.StartGatewayAsync();

        var registered = await gateway.Client.PostAsJsonAsync(
            $"{AuthEndpoints.BasePath}/register",
            new CredentialsRequest("someone@flashflights.test", "Not-The-Demo-1"));

        Assert.Equal(HttpStatusCode.OK, registered.StatusCode);
        Assert.False(await SeedAsync(gateway));

        await using var scope = gateway.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FlashFlightsIdentityDbContext>();

        Assert.Equal("someone@flashflights.test", (await db.Users.SingleAsync()).Email);
    }

    private static async Task<bool> SeedAsync(AuthTestServer gateway)
    {
        await using var scope = gateway.Services.CreateAsyncScope();

        return await ActivatorUtilities
            .CreateInstance<IdentityDemoSeeder>(scope.ServiceProvider)
            .SeedAsync(World());
    }
}
