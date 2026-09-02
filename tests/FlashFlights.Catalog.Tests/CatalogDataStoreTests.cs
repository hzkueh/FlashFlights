using FlashFlights.Catalog.Persistence;
using FlashFlights.ServiceDefaults.DataStores;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FlashFlights.Catalog.Tests;

/// <summary>
/// The startup migration and readiness probe are shared by every service, so
/// they are exercised here once against a real store rather than four times
/// against fakes. Catalog is the cheapest place to do it: a SQLite file needs
/// no container.
/// </summary>
public class CatalogDataStoreTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), $"flashflights-catalog-{Guid.NewGuid():N}");

    /// <summary>
    /// The acceptance criterion for ticket 02: a fresh clone comes up with its
    /// tables and no manual migration step.
    /// </summary>
    [Fact]
    public async Task Migrating_an_empty_store_creates_the_catalog_schema()
    {
        Directory.CreateDirectory(_directory);
        await using var services = BuildServices(DatabaseFile());

        await MigratorFor(services).MigrateAsync(CancellationToken.None);

        Assert.Equal(
            ["FlightSeatCounts", "Flights"],
            await TableNamesAsync(services));
    }

    /// <summary>Containers restart; the second start must not fail or wipe anything.</summary>
    [Fact]
    public async Task Migrating_an_already_migrated_store_changes_nothing()
    {
        Directory.CreateDirectory(_directory);
        await using var services = BuildServices(DatabaseFile());
        var migrator = MigratorFor(services);

        await migrator.MigrateAsync(CancellationToken.None);
        await SeedOneFlightAsync(services);
        await migrator.MigrateAsync(CancellationToken.None);

        await using var scope = services.CreateAsyncScope();
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().Flights.CountAsync());
    }

    [Fact]
    public async Task Probe_succeeds_once_the_store_is_there()
    {
        Directory.CreateDirectory(_directory);
        await using var services = BuildServices(DatabaseFile());
        await MigratorFor(services).MigrateAsync(CancellationToken.None);

        await ProbeFor(services).ConnectAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Probe_fails_when_the_store_cannot_be_reached()
    {
        // The directory is deliberately never created, so SQLite cannot open the file.
        await using var services = BuildServices(DatabaseFile());

        await Assert.ThrowsAnyAsync<Exception>(
            () => ProbeFor(services).ConnectAsync(CancellationToken.None));
    }

    [Fact]
    public void Refuses_to_start_without_a_configured_connection_string()
    {
        var configuration = new ConfigurationBuilder().Build();

        Assert.Throws<InvalidOperationException>(() => configuration.RequireConnectionString("CatalogDb"));
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);

        // Connections return to the SQLite pool, which keeps the file handle
        // open until the pool is cleared.
        SqliteConnection.ClearAllPools();

        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private string DatabaseFile() => Path.Combine(_directory, "catalog.db");

    private static IDataStoreMigrator MigratorFor(IServiceProvider services) =>
        new DbContextDataStoreMigrator<CatalogDbContext>(
            "catalog-sqlite", services.GetRequiredService<IServiceScopeFactory>());

    private static IDataStoreProbe ProbeFor(IServiceProvider services) =>
        new DbContextDataStoreProbe<CatalogDbContext>(
            "catalog-sqlite", services.GetRequiredService<IServiceScopeFactory>());

    private static ServiceProvider BuildServices(string databaseFile) =>
        new ServiceCollection()
            .AddDbContext<CatalogDbContext>(options => options.UseSqlite($"Data Source={databaseFile}"))
            .BuildServiceProvider();

    private static async Task SeedOneFlightAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();

        db.Flights.Add(new Domain.Flight
        {
            Id = Guid.NewGuid(),
            FlightNumber = "FF412",
            Origin = "LHR",
            Destination = "BCN",
            DepartureAt = DateTimeOffset.UtcNow.AddDays(30),
            FlashPrice = 49.99m,
            SaleStartsAt = DateTimeOffset.UtcNow,
            SaleEndsAt = DateTimeOffset.UtcNow.AddHours(6),
        });

        await db.SaveChangesAsync();
    }

    private static async Task<string[]> TableNamesAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();

        await using var connection = new SqliteConnection(db.Database.GetConnectionString());
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        // sqlite_% and __EF% are SQLite's and EF's own bookkeeping, not schema
        // this ticket is responsible for.
        command.CommandText =
            """
            SELECT name FROM sqlite_master
            WHERE type = 'table' AND name NOT LIKE 'sqlite_%' AND name NOT LIKE '__EF%'
            ORDER BY name
            """;

        var names = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            names.Add(reader.GetString(0));
        }

        return [.. names];
    }
}
