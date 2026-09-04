using FlashFlights.Gateway.Identity;
using FlashFlights.ServiceDefaults;
using FlashFlights.ServiceDefaults.Authentication;
using FlashFlights.ServiceDefaults.DataStores;
using Microsoft.EntityFrameworkCore;

const string SpaCorsPolicy = "spa";

var builder = WebApplication.CreateBuilder(args);

// The gateway hosts the one shared user store — Identity is a lightweight
// piece alongside the gateway, not a fourth microservice. It takes no part in
// the bus, which is why the datastore registration is separate from the rest
// of the service defaults.
builder.Services.AddSingleton(new ServiceIdentity("gateway"));
builder.AddFlashFlightsDataStore<FlashFlightsIdentityDbContext>(
    "identity-sqlite",
    options => options.UseSqlite(builder.Configuration.RequireConnectionString("IdentityDb")));

// Identity itself, plus the token issuance that comes with owning the user
// store: the gateway is the only issuer in the system, and the services only
// ever validate.
builder.AddFlashFlightsIdentity();

// And validation, which the gateway needs for /api/auth/me. Called here rather
// than folded into the line above, so moving the user store elsewhere one day
// cannot quietly take the gateway's ability to reject a token with it. The
// three services get this from AddFlashFlightsServiceDefaults instead, which
// the gateway does not take: it plays no part in the bus.
builder.AddFlashFlightsJwtAuthentication();

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

// Every supported way of running FlashFlights puts the browser on the gateway's
// own origin, so this policy is not load-bearing today (ticket 03 settled that).
// It is kept as the one switch that lets a separately hosted SPA talk to this
// gateway without a code change — named origins rather than AllowAnyOrigin,
// because such a client would hold a JWT and open a SignalR connection, and
// credentialed requests forbid the wildcard.
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? ["http://localhost:5173"];

builder.Services.AddCors(options => options.AddPolicy(SpaCorsPolicy, policy => policy
    .WithOrigins(allowedOrigins)
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()));

var app = builder.Build();

app.UseCors(SpaCorsPolicy);

// The gateway's own health, distinct from the services it fronts. Mapped
// before the proxy so these paths are answered here rather than forwarded.
app.MapFlashFlightsHealth();

// Answered here rather than forwarded: the one shared user store is hosted
// alongside the gateway, so register and login live here too.
app.MapFlashFlightsAuth();

app.MapReverseProxy();

app.Run();
