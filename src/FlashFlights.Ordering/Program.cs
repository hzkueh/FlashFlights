using FlashFlights.Ordering.Infrastructure;
using FlashFlights.ServiceDefaults;
using FlashFlights.ServiceDefaults.DataStores;
using FlashFlights.ServiceDefaults.Wiring;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<IDataStoreProbe, PostgresDataStoreProbe>();
builder.Services.AddSingleton<IPingLog, InMemoryPingLog>();
builder.AddFlashFlightsServiceDefaults("ordering", bus => bus.AddConsumer<PingSentConsumer>());

var app = builder.Build();

app.MapFlashFlightsHealth();

// Ticket-01 wiring probe: what this service actually consumed off the bus.
// Remove with PingSent.
app.MapGet("/_ping/received", (IPingLog log) => log.Received);

app.Run();
