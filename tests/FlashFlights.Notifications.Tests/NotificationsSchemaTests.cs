using FlashFlights.Notifications.Domain;
using FlashFlights.Notifications.Persistence;
using FlashFlights.ServiceDefaults.DataStores;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FlashFlights.Notifications.Tests;

public class NotificationsSchemaTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), $"flashflights-notifications-{Guid.NewGuid():N}");

    [Fact]
    public async Task Migrating_an_empty_store_creates_the_watch_and_notification_tables()
    {
        await using var services = BuildServices();

        await MigrateAsync(services);

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();

        Assert.Empty(await db.Watches.ToListAsync());
        Assert.Empty(await db.Notifications.ToListAsync());
    }

    /// <summary>
    /// Watching twice is the same subscription. Enforcing it here rather than
    /// in the endpoint means a double-click cannot produce two notifications
    /// for one sale.
    /// </summary>
    [Fact]
    public async Task A_user_cannot_watch_the_same_flight_twice()
    {
        var userId = Guid.NewGuid();
        var flightId = Guid.NewGuid();

        await using var services = BuildServices();
        await MigrateAsync(services);

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        db.Watches.Add(AWatch(userId, flightId));
        db.Watches.Add(AWatch(userId, flightId));

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    /// <summary>
    /// A Flight's sale goes live once, so one notification per (User, Flight)
    /// is the whole truth — and a redelivered FlightSaleStarted cannot
    /// duplicate anyone's inbox.
    /// </summary>
    [Fact]
    public async Task A_redelivered_sale_notification_cannot_duplicate_an_inbox()
    {
        var userId = Guid.NewGuid();
        var flightId = Guid.NewGuid();

        await using var services = BuildServices();
        await MigrateAsync(services);

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        db.Notifications.Add(ASaleStartedNotification(userId, flightId));
        await db.SaveChangesAsync();

        db.Notifications.Add(ASaleStartedNotification(userId, flightId));

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
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

    private static Watch AWatch(Guid userId, Guid flightId) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        FlightId = flightId,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private static Notification ASaleStartedNotification(Guid userId, Guid flightId) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        FlightId = flightId,
        Body = "FF412 LHR to BCN is now on sale",
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private static Task MigrateAsync(IServiceProvider services) =>
        new DbContextDataStoreMigrator<NotificationsDbContext>(
                "notifications-sqlite", services.GetRequiredService<IServiceScopeFactory>())
            .MigrateAsync(CancellationToken.None);

    private ServiceProvider BuildServices()
    {
        Directory.CreateDirectory(_directory);

        return new ServiceCollection()
            .AddDbContext<NotificationsDbContext>(options =>
                options.UseSqlite($"Data Source={Path.Combine(_directory, "notifications.db")}"))
            .BuildServiceProvider();
    }
}
