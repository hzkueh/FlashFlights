using FlashFlights.Gateway.Identity;
using FlashFlights.ServiceDefaults.DataStores;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FlashFlights.Gateway.Tests;

/// <summary>
/// The user store is hosted alongside the gateway rather than as a fourth
/// microservice, so the gateway migrates it on startup like any other service.
/// </summary>
public class IdentitySchemaTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), $"flashflights-identity-{Guid.NewGuid():N}");

    [Fact]
    public async Task Migrating_an_empty_store_creates_the_user_tables()
    {
        await using var services = BuildServices();

        await MigrateAsync(services);

        await using var scope = services.CreateAsyncScope();
        Assert.Empty(
            await scope.ServiceProvider.GetRequiredService<FlashFlightsIdentityDbContext>().Users.ToListAsync());
    }

    /// <summary>
    /// Users are keyed by Guid, not Identity's default string, so the UserId
    /// that Ordering and Notifications store is the same type on both sides.
    /// </summary>
    [Fact]
    public void Users_are_keyed_by_guid()
    {
        using var db = BuildContext(Path.Combine(_directory, "identity.db"));

        var key = db.Model.FindEntityType(typeof(FlashFlightsUser))!.FindPrimaryKey()!;

        Assert.Equal(typeof(Guid), Assert.Single(key.Properties).ClrType);
    }

    /// <summary>There is no admin role and no roles at all, so the role tables would only ever be empty.</summary>
    [Fact]
    public void Store_carries_no_role_tables()
    {
        using var db = BuildContext(Path.Combine(_directory, "identity.db"));

        Assert.DoesNotContain(
            db.Model.GetEntityTypes().Select(type => type.GetTableName()),
            table => table?.Contains("Role", StringComparison.Ordinal) == true);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        SqliteConnection.ClearAllPools();

        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static Task MigrateAsync(IServiceProvider services) =>
        new DbContextDataStoreMigrator<FlashFlightsIdentityDbContext>(
                "identity-sqlite", services.GetRequiredService<IServiceScopeFactory>())
            .MigrateAsync(CancellationToken.None);

    private static FlashFlightsIdentityDbContext BuildContext(string databaseFile) =>
        new(new DbContextOptionsBuilder<FlashFlightsIdentityDbContext>()
            .UseSqlite($"Data Source={databaseFile}")
            .Options);

    private ServiceProvider BuildServices()
    {
        Directory.CreateDirectory(_directory);

        return new ServiceCollection()
            .AddDbContext<FlashFlightsIdentityDbContext>(options =>
                options.UseSqlite($"Data Source={Path.Combine(_directory, "identity.db")}"))
            .BuildServiceProvider();
    }
}
