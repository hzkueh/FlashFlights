using FlashFlights.Notifications.Persistence;
using FlashFlights.ServiceDefaults;
using FlashFlights.ServiceDefaults.DataStores;
using FlashFlights.ServiceDefaults.Wiring;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddFlashFlightsDataStore<NotificationsDbContext>(
    "notifications-sqlite",
    options => options.UseSqlite(builder.Configuration.RequireConnectionString("NotificationsDb")));

builder.Services.AddSingleton<IPingLog, InMemoryPingLog>();
builder.AddFlashFlightsServiceDefaults("notifications", bus => bus.AddConsumer<PingSentConsumer>());

var app = builder.Build();

app.MapFlashFlightsHealth();

// Ticket-01 wiring probe: what this service actually consumed off the bus.
// Remove with PingSent.
app.MapGet("/_ping/received", (IPingLog log) => log.Received);

app.Run();
