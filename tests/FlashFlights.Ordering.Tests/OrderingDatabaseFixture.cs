using FlashFlights.Ordering.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace FlashFlights.Ordering.Tests;

/// <summary>
/// One throwaway PostgreSQL container for the whole assembly, migrated once. It
/// is PostgreSQL rather than SQLite on purpose: the no-double-hold guarantee is
/// about real row-level locking, which SQLite serialises away — it would pass
/// while proving nothing (ADR-0001).
///
/// Tests share the container but not data: each seeds its own Flight and Seats
/// under fresh Guids, so they never collide and no truncation between tests is
/// needed.
/// </summary>
public sealed class OrderingDatabaseFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        // Matches the image docker-compose runs Ordering against.
        .WithImage("postgres:17-alpine")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        await using var db = NewDbContext();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();

    /// <summary>
    /// A fresh context on its own connection. The concurrency test needs several
    /// at once, each running its CreateHold on a separate connection, which is
    /// the only way the row-lock contention it asserts can actually occur.
    /// </summary>
    public OrderingDbContext NewDbContext() =>
        new(new DbContextOptionsBuilder<OrderingDbContext>()
            .UseNpgsql(ConnectionString)
            .Options);

    /// <summary>
    /// A migrated database of its own on the same container, and a factory for
    /// contexts on it.
    ///
    /// <para>
    /// The exception to "tests share the container but not data". That works
    /// because every other test here scopes itself to Seats it created under
    /// fresh Guids — but the demo seeder's question is about the whole store
    /// ("has anything been seeded here?"), which no amount of fresh Guids can
    /// isolate. So those tests get an empty store rather than a private corner
    /// of a shared one.
    /// </para>
    /// </summary>
    public async Task<Func<OrderingDbContext>> NewEmptyStoreAsync()
    {
        var databaseName = $"ordering_{Guid.NewGuid():N}";

        await using (var admin = new NpgsqlConnection(ConnectionString))
        {
            await admin.OpenAsync();

            // Not parameterisable: CREATE DATABASE takes an identifier, not a
            // value. The name is a Guid this method generated, so there is
            // nothing here an input could reach.
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", admin);
            await create.ExecuteNonQueryAsync();
        }

        var connectionString = new NpgsqlConnectionStringBuilder(ConnectionString)
        {
            Database = databaseName,
        }.ConnectionString;

        OrderingDbContext NewContext() =>
            new(new DbContextOptionsBuilder<OrderingDbContext>().UseNpgsql(connectionString).Options);

        await using (var db = NewContext())
        {
            await db.Database.MigrateAsync();
        }

        return NewContext;
    }
}

/// <summary>Binds the fixture to every integration test class that needs the database.</summary>
[CollectionDefinition(Name)]
public sealed class OrderingDatabaseCollection : ICollectionFixture<OrderingDatabaseFixture>
{
    public const string Name = "ordering-database";
}
