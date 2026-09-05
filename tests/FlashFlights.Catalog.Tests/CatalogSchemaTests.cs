using FlashFlights.Catalog.Domain;
using FlashFlights.Catalog.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace FlashFlights.Catalog.Tests;

public class CatalogSchemaTests
{
    [Fact]
    public void Flight_carries_the_route_departure_price_and_sale_window()
    {
        Assert.Equal(
            [
                "DepartureAt",
                "Destination",
                "FlashPrice",
                "FlightNumber",
                "Id",
                "Origin",
                "ReferenceFare",
                "SaleEndsAt",
                "SaleStartsAt",
            ],
            MappedPropertyNames<Flight>());
    }

    /// <summary>
    /// Catalog's projection is counts, and only counts. A per-Seat status here
    /// would be exactly the stored, mutable SeatStatus that ADR-0001 rules out —
    /// and because this projection is eventually consistent, it would be a
    /// stale one that browsing could mistake for the truth.
    /// </summary>
    [Fact]
    public void Seat_projection_holds_counts_and_never_per_seat_status()
    {
        Assert.Equal(
            ["AvailableSeats", "ConfirmedSeats", "FlightId", "HeldSeats", "LastMovementAt", "TotalSeats"],
            MappedPropertyNames<FlightSeatCounts>());
    }

    /// <summary>Ordering owns Seats; Catalog holding rows for them would make it a second source of truth.</summary>
    [Fact]
    public void Catalog_stores_no_seats_of_its_own()
    {
        Assert.Equal(
            ["FlightSeatCounts", "Flights"],
            Model().GetEntityTypes().Select(type => type.GetTableName()!).Order().ToArray());
    }

    private static string[] MappedPropertyNames<TEntity>() =>
        [.. Model().FindEntityType(typeof(TEntity))!.GetProperties().Select(property => property.Name).Order()];

    private static IModel Model()
    {
        using var db = new CatalogDbContext(new DbContextOptionsBuilder<CatalogDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options);

        return db.Model;
    }
}
