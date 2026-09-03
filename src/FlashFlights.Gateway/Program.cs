using FlashFlights.Gateway.Identity;
using FlashFlights.ServiceDefaults;
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

app.MapReverseProxy();

app.Run();
