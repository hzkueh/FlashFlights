using FlashFlights.Catalog.Infrastructure;
using FlashFlights.Contracts;
using FlashFlights.ServiceDefaults;
using FlashFlights.ServiceDefaults.DataStores;
using MassTransit;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<IDataStoreProbe, SqliteDataStoreProbe>();
builder.AddFlashFlightsServiceDefaults("catalog");

var app = builder.Build();

app.MapFlashFlightsHealth();

// Ticket-01 wiring probe: publishes onto the bus so Ordering and Notifications
// can be observed consuming it. Remove with PingSent.
app.MapPost("/_ping", async (IPublishEndpoint publish, ServiceIdentity identity) =>
{
    var ping = new PingSent(Guid.NewGuid(), identity.Name, DateTimeOffset.UtcNow);
    await publish.Publish(ping);

    return Results.Accepted(value: ping);
});

app.Run();
