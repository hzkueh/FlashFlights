using FlashFlights.Catalog.Browsing;
using FlashFlights.Catalog.Persistence;
using FlashFlights.Catalog.Projection;
using FlashFlights.Catalog.Sales;
using FlashFlights.Catalog.Seeding;
using FlashFlights.DemoData;
using FlashFlights.ServiceDefaults;
using FlashFlights.ServiceDefaults.DataStores;
using MassTransit;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddFlashFlightsDataStore<CatalogDbContext>(
    "catalog-sqlite",
    options => options.UseSqlite(builder.Configuration.RequireConnectionString("CatalogDb")));

// A fresh clone comes up with a catalog in it: five Flights spanning upcoming,
// live, and ended sales, seeded once and left alone on every restart after
// (ticket 11). There is no flight-authoring UI to fill it any other way
// (CONTEXT.md), so without this the list page is empty until a test runs.
builder.AddFlashFlightsDemoSeed<CatalogDemoSeeder>();

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

builder.Services.AddOptions<SaleStartSchedulerOptions>()
    .Bind(builder.Configuration.GetSection(SaleStartSchedulerOptions.SectionName))
    .ValidateDataAnnotations()
    .Validate(
        options => options.Interval > TimeSpan.Zero,
        "SaleStartScheduler:Interval must be positive; it is how long a watcher waits to be told.")
    .ValidateOnStart();

// A sale opening announces itself onto the bus for Notifications to turn into
// alerts (ticket 09). Behind the notifier seam, so the announcer itself stays
// unaware of MassTransit — and unlike the seat-movement publish, a failure here
// is surfaced rather than swallowed: the announcement is the feature.
builder.Services.AddScoped<IFlightSaleNotifier, MassTransitFlightSaleNotifier>();
builder.Services.AddScoped<SaleStartAnnouncer>();

// The scheduler that detects a Flight crossing its SaleStartsAt. Load-bearing:
// nothing on any read path recomputes "this sale just opened", so a crossing
// this loop never scans is an alert that never fires.
builder.Services.AddHostedService<SaleStartScheduler>();

var app = builder.Build();

app.MapFlashFlightsHealth();

app.MapFlashFlightsCatalog();

app.Run();
