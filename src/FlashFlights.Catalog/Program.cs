using FlashFlights.Catalog.Browsing;
using FlashFlights.Catalog.Persistence;
using FlashFlights.Catalog.Projection;
using FlashFlights.ServiceDefaults;
using FlashFlights.ServiceDefaults.DataStores;
using MassTransit;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddFlashFlightsDataStore<CatalogDbContext>(
    "catalog-sqlite",
    options => options.UseSqlite(builder.Configuration.RequireConnectionString("CatalogDb")));

// The three consumers that keep the SeatCounts projection fresh from Ordering's
// movements. Registered on the bus here; each gets its own prefixed queue.
builder.AddFlashFlightsServiceDefaults("catalog", bus =>
{
    bus.AddConsumer<SeatsHeldConsumer>();
    bus.AddConsumer<SeatsReleasedConsumer>();
    bus.AddConsumer<SeatsConfirmedConsumer>();
});

// The list page's sale-state comes from comparing the sale window to the clock,
// so the clock is a registered dependency the browse service reads.
builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddScoped<SeatCountsProjector>();
builder.Services.AddScoped<IFlightCatalogService, FlightCatalogService>();

var app = builder.Build();

app.MapFlashFlightsHealth();

app.MapFlashFlightsCatalog();

app.Run();
