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

// Named origins rather than AllowAnyOrigin: the SPA will hold a JWT and open a
// SignalR connection, and credentialed requests forbid the wildcard.
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
