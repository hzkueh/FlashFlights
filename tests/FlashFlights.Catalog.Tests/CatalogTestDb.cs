using FlashFlights.Catalog.Domain;
using FlashFlights.Catalog.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FlashFlights.Catalog.Tests;

/// <summary>
/// A CatalogDbContext on a private in-memory SQLite connection, kept open for
/// the life of the handle so the schema and rows survive between operations. One
/// per test — no shared state to reset.
/// </summary>
internal sealed class CatalogTestDb : IDisposable
{
    private readonly SqliteConnection _connection;

    private CatalogTestDb(SqliteConnection connection) => _connection = connection;

    public static CatalogTestDb Create()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        using (var db = ContextOn(connection))
        {
            db.Database.EnsureCreated();
        }

        return new CatalogTestDb(connection);
    }

    /// <summary>A fresh context on the shared connection — mirrors a scoped context per request.</summary>
    public CatalogDbContext NewContext() => ContextOn(_connection);

    /// <summary>Seeds one Flight and, unless suppressed, its all-Available SeatCounts baseline.</summary>
    public async Task<Guid> SeedFlightAsync(
        string flightNumber = "FF412",
        DateTimeOffset? saleStartsAt = null,
        DateTimeOffset? saleEndsAt = null,
        int? totalSeats = 12,
        DateTimeOffset? countsBaselineAt = null,
        decimal? referenceFare = null)
    {
        var flightId = Guid.NewGuid();
        await using var db = NewContext();

        db.Flights.Add(new Flight
        {
            Id = flightId,
            FlightNumber = flightNumber,
            Origin = "LHR",
            Destination = "BCN",
            DepartureAt = DateTimeOffset.UtcNow.AddDays(30),
            FlashPrice = 49.99m,
            ReferenceFare = referenceFare,
            SaleStartsAt = saleStartsAt ?? DateTimeOffset.UtcNow.AddHours(-1),
            SaleEndsAt = saleEndsAt ?? DateTimeOffset.UtcNow.AddHours(5),
        });

        if (totalSeats is { } total)
        {
            db.FlightSeatCounts.Add(new FlightSeatCounts
            {
                FlightId = flightId,
                TotalSeats = total,
                AvailableSeats = total,
                HeldSeats = 0,
                ConfirmedSeats = 0,
                // Before any movement, so the first real event is always newer.
                LastMovementAt = countsBaselineAt ?? DateTimeOffset.UnixEpoch,
            });
        }

        await db.SaveChangesAsync();
        return flightId;
    }

    public async Task<FlightSeatCounts> CountsAsync(Guid flightId)
    {
        await using var db = NewContext();
        return await db.FlightSeatCounts.SingleAsync(c => c.FlightId == flightId);
    }

    public void Dispose() => _connection.Dispose();

    private static CatalogDbContext ContextOn(SqliteConnection connection) =>
        new(new DbContextOptionsBuilder<CatalogDbContext>().UseSqlite(connection).Options);
}
