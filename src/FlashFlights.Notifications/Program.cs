using System.Text.Json.Serialization;
using FlashFlights.Notifications.Persistence;
using FlashFlights.Notifications.SeatMaps;
using FlashFlights.ServiceDefaults;
using FlashFlights.ServiceDefaults.DataStores;
using FlashFlights.ServiceDefaults.Wiring;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddFlashFlightsDataStore<NotificationsDbContext>(
    "notifications-sqlite",
    options => options.UseSqlite(builder.Configuration.RequireConnectionString("NotificationsDb")));

builder.Services.AddSingleton<IPingLog, InMemoryPingLog>();

// The live seat map: three consumers relay Ordering's movements to connected
// viewers, so they register on the bus here alongside the ticket-01 ping probe.
builder.AddFlashFlightsServiceDefaults("notifications", bus =>
{
    bus.AddConsumer<PingSentConsumer>();
    bus.AddConsumer<SeatsHeldLiveConsumer>();
    bus.AddConsumer<SeatsReleasedLiveConsumer>();
    bus.AddConsumer<SeatsConfirmedLiveConsumer>();
});

// SeatStatus travels to the SPA by name (Available/Held/Confirmed), matching its
// SeatStatus union, rather than as the enum's ordinal.
builder.Services
    .AddSignalR()
    .AddJsonProtocol(options => options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddSingleton<ISeatMapBroadcaster, SignalRSeatMapBroadcaster>();

var app = builder.Build();

app.MapFlashFlightsHealth();

// The hub the SPA holds open while a seat map is on screen. Reached through the
// gateway at /api/notifications/hubs/seat-map; anonymous, like the seat map read
// it decorates.
app.MapHub<SeatMapHub>("/hubs/seat-map");

// Ticket-01 wiring probe: what this service actually consumed off the bus.
// Remove with PingSent.
app.MapGet("/_ping/received", (IPingLog log) => log.Received);

app.Run();
