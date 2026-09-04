using System.Security.Claims;
using FlashFlights.Gateway.Identity;
using FlashFlights.ServiceDefaults.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FlashFlights.Gateway.Tests;

/// <summary>
/// The signing config the auth suites share, and hosts wired the two ways
/// FlashFlights wires them: the gateway, which signs and validates, and a
/// service, which only validates.
///
/// Real hosts driven over HTTP rather than handlers called directly — a 401 is
/// decided by authentication middleware, so a test that never goes through the
/// pipeline cannot tell a protected endpoint from an open one.
/// </summary>
internal static class AuthTestHost
{
    /// <summary>The endpoint <see cref="StartServiceAsync"/> protects.</summary>
    public const string ProtectedPath = "/seats/held-by-me";

    /// <summary>
    /// A fresh copy each call, distinct from the shipped development values —
    /// so a test cannot pass by picking those up, and a test that mutates what
    /// it is given cannot reach into another's.
    /// </summary>
    public static JwtOptions NewSigningConfig() => new()
    {
        Issuer = "flashflights-tests",
        Audience = "flashflights-tests-spa",
        SigningKey = "a-test-signing-key-well-past-the-hmac-sha256-minimum",
        Lifetime = TimeSpan.FromHours(2),
    };

    /// <summary>
    /// The gateway's auth surface: the shared user store, token issuance, and
    /// <c>/api/auth</c>. Configured through the same extensions
    /// <c>Program.cs</c> calls, so this exercises the shipped wiring rather than
    /// a test-only rebuild of it.
    /// </summary>
    public static async Task<AuthTestServer> StartGatewayAsync(JwtOptions? signingConfig = null)
    {
        // Held open for the host's lifetime: an in-memory SQLite database exists
        // only while a connection to it does.
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var builder = CreateBuilder(signingConfig);
        builder.Services.AddDbContext<FlashFlightsIdentityDbContext>(options => options.UseSqlite(connection));
        builder.AddFlashFlightsIdentity();
        builder.AddFlashFlightsJwtAuthentication();

        var app = builder.Build();

        await using (var scope = app.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<FlashFlightsIdentityDbContext>()
                .Database.MigrateAsync();
        }

        app.MapFlashFlightsAuth();

        return await StartAsync(app, connection);
    }

    /// <summary>
    /// A service behind the gateway: bearer validation against the shared
    /// signing config and one endpoint that requires it, with no user store and
    /// no ability to issue anything.
    ///
    /// Adds no authentication middleware of its own, on purpose — the services
    /// do not either, so if <c>WebApplication</c> ever stopped inserting it for
    /// them, these tests are what would notice.
    /// </summary>
    public static async Task<AuthTestServer> StartServiceAsync(JwtOptions? signingConfig = null)
    {
        var builder = CreateBuilder(signingConfig);
        builder.AddFlashFlightsJwtAuthentication();

        var app = builder.Build();

        app.MapGet(ProtectedPath, (ClaimsPrincipal principal) => new { userId = principal.RequireUserId() })
            .RequireAuthorization();

        return await StartAsync(app, connection: null);
    }

    /// <summary>
    /// A token as the gateway would have signed it, for cases a client cannot
    /// reach through <c>/api/auth</c> — an expired one, or one signed with a key
    /// the validating host has never heard of.
    /// </summary>
    public static AccessToken IssueToken(
        JwtOptions signingConfig,
        DateTimeOffset? issuedAt = null,
        string email = "issued@flashflights.test") =>
        new JwtAccessTokenIssuer(
                Options.Create(signingConfig),
                new FixedClock(issuedAt ?? DateTimeOffset.UtcNow))
            .Issue(new FlashFlightsUser { Id = Guid.CreateVersion7(), UserName = email, Email = email });

    private static WebApplicationBuilder CreateBuilder(JwtOptions? signingConfig)
    {
        var jwt = signingConfig ?? NewSigningConfig();
        var builder = WebApplication.CreateSlimBuilder();

        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"{JwtOptions.SectionName}:Issuer"] = jwt.Issuer,
            [$"{JwtOptions.SectionName}:Audience"] = jwt.Audience,
            [$"{JwtOptions.SectionName}:SigningKey"] = jwt.SigningKey,
            [$"{JwtOptions.SectionName}:Lifetime"] = jwt.Lifetime.ToString(),
        });

        builder.WebHost.UseTestServer();

        return builder;
    }

    private static async Task<AuthTestServer> StartAsync(WebApplication app, SqliteConnection? connection)
    {
        await app.StartAsync();

        return new AuthTestServer(app, app.GetTestClient(), connection);
    }
}

/// <summary>A started host and a client pointed at it.</summary>
internal sealed class AuthTestServer(WebApplication app, HttpClient client, SqliteConnection? connection)
    : IAsyncDisposable
{
    public HttpClient Client { get; } = client;

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await app.DisposeAsync();

        if (connection is not null)
        {
            await connection.DisposeAsync();
        }
    }
}

/// <summary>A clock that does not move, so an expiry is a fact rather than a race.</summary>
internal sealed class FixedClock(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
