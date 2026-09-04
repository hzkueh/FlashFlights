using FlashFlights.Ordering.Holds;
using FlashFlights.Ordering.Persistence;
using FlashFlights.ServiceDefaults;
using FlashFlights.ServiceDefaults.DataStores;
using FlashFlights.ServiceDefaults.Wiring;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// PostgreSQL, alone among the services: granting a Hold takes real row-level
// locks over the SeatMovement ledger, which SQLite cannot express (ADR-0001).
builder.AddFlashFlightsDataStore<OrderingDbContext>(
    "ordering-postgres",
    options => options.UseNpgsql(builder.Configuration.RequireConnectionString("OrderingDb")));

builder.Services.AddSingleton<IPingLog, InMemoryPingLog>();
builder.AddFlashFlightsServiceDefaults("ordering", bus => bus.AddConsumer<PingSentConsumer>());

// The read side and the sweep must read one clock, or a Hold could be expired to
// a reader and live to a confirm — so the clock is a registered dependency both
// share, not DateTimeOffset.UtcNow scattered through the code.
builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddOptions<HoldOptions>()
    .Bind(builder.Configuration.GetSection(HoldOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<HoldSweepOptions>()
    .Bind(builder.Configuration.GetSection(HoldSweepOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

// Cross-checks the sweep interval against the TTL, which a data annotation on one
// options type cannot see — a sweep no more frequent than expiry defeats itself.
builder.Services.AddSingleton<IValidateOptions<HoldSweepOptions>, ValidateHoldSweepOptions>();

builder.Services.AddScoped<IHoldService, HoldService>();

// The sweep that posts Released movements for silently expired Holds, so Catalog
// learns of them (ADR-0001). The read side is already correct without it; this is
// what keeps browsing counts fresh.
builder.Services.AddHostedService<HoldExpirySweep>();

var app = builder.Build();

app.MapFlashFlightsHealth();

app.MapFlashFlightsHolds();

// Ticket-01 wiring probe: what this service actually consumed off the bus.
// Remove with PingSent.
app.MapGet("/_ping/received", (IPingLog log) => log.Received);

app.Run();
