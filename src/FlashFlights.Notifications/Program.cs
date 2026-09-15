using System.Text.Json.Serialization;
using FlashFlights.Notifications.Persistence;
using FlashFlights.Notifications.SeatMaps;
using FlashFlights.Notifications.Watching;
using FlashFlights.ServiceDefaults;
using FlashFlights.ServiceDefaults.DataStores;
using FlashFlights.ServiceDefaults.Wiring;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddFlashFlightsDataStore<NotificationsDbContext>(
    "notifications-sqlite",
    options => options.UseSqlite(builder.Configuration.RequireConnectionString("NotificationsDb")));

builder.Services.AddSingleton<IPingLog, InMemoryPingLog>();

// The live seat map: three consumers relay Ordering's movements to connected
// viewers. Alongside them, the announcement that turns a Watch into an alert.
builder.AddFlashFlightsServiceDefaults("notifications", bus =>
{
    bus.AddConsumer<PingSentConsumer>();
    bus.AddConsumer<SeatsHeldLiveConsumer>();
    bus.AddConsumer<SeatsReleasedLiveConsumer>();
    bus.AddConsumer<SeatsConfirmedLiveConsumer>();
    bus.AddConsumer<FlightSaleStartedConsumer>();
});

// Must follow the service defaults, which register the scheme this configures:
// the notifications hub is authenticated, and a browser cannot put a header on a
// WebSocket handshake.
builder.AddNotificationsHubAuthentication();

// SeatStatus travels to the SPA by name (Available/Held/Confirmed), matching its
// SeatStatus union, rather than as the enum's ordinal.
builder.Services
    .AddSignalR()
    .AddJsonProtocol(options => options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// Which User a hub connection belongs to. Without it SignalR looks for a claim a
// FlashFlights token does not carry, and every per-User push would silently
// reach no one.
builder.Services.AddSingleton<IUserIdProvider, FlashFlightsUserIdProvider>();

builder.Services.AddSingleton<ISeatMapBroadcaster, SignalRSeatMapBroadcaster>();
builder.Services.AddSingleton<INotificationPusher, SignalRNotificationPusher>();

// A Notification is stamped with this service's clock, so it is a registered
// dependency rather than DateTimeOffset.UtcNow scattered through the code.
builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddScoped<IWatchService, WatchService>();
builder.Services.AddScoped<INotificationInbox, NotificationInbox>();
builder.Services.AddScoped<IWatchNotificationDispatcher, WatchNotificationDispatcher>();

var app = builder.Build();

app.MapFlashFlightsHealth();

// The hub the SPA holds open while a seat map is on screen. Reached through the
// gateway at /api/notifications/hubs/seat-map; anonymous, like the seat map read
// it decorates.
app.MapHub<SeatMapHub>("/hubs/seat-map");

// The hub a signed-in SPA holds open for its own alerts. Authenticated and
// addressed per User, unlike the seat map's anonymous per-Flight groups.
app.MapHub<NotificationsHub>(NotificationsHubAuthentication.HubPath);

// Watches and the Notification inbox, both scoped to the token's User.
app.MapFlashFlightsWatches();

// Ticket-01 wiring probe: what this service actually consumed off the bus.
// Remove with PingSent.
app.MapGet("/_ping/received", (IPingLog log) => log.Received);

app.Run();
