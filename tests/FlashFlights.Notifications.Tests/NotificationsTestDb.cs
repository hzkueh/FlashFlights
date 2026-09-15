using FlashFlights.Notifications.Domain;
using FlashFlights.Notifications.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FlashFlights.Notifications.Tests;

/// <summary>
/// A NotificationsDbContext on a private in-memory SQLite connection, kept open
/// for the life of the handle so the schema and rows survive between operations.
/// One per test — no shared state to reset. Mirrors Catalog's test db.
/// </summary>
internal sealed class NotificationsTestDb : IDisposable
{
    private readonly SqliteConnection _connection;

    private NotificationsTestDb(SqliteConnection connection) => _connection = connection;

    public static NotificationsTestDb Create()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        using (var db = ContextOn(connection))
        {
            db.Database.EnsureCreated();
        }

        return new NotificationsTestDb(connection);
    }

    /// <summary>A fresh context on the shared connection — mirrors a scoped context per request.</summary>
    public NotificationsDbContext NewContext() => ContextOn(_connection);

    public async Task WatchAsync(Guid userId, Guid flightId)
    {
        await using var db = NewContext();

        db.Watches.Add(new Watch
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            FlightId = flightId,
            CreatedAt = DateTimeOffset.UnixEpoch,
        });

        await db.SaveChangesAsync();
    }

    /// <summary>Seeds one Notification and returns its id.</summary>
    public async Task<Guid> NotifyAsync(
        Guid userId,
        string body,
        DateTimeOffset? createdAt = null,
        DateTimeOffset? readAt = null)
    {
        var notificationId = Guid.NewGuid();
        await using var db = NewContext();

        db.Notifications.Add(new Notification
        {
            Id = notificationId,
            UserId = userId,
            FlightId = Guid.NewGuid(),
            Body = body,
            CreatedAt = createdAt ?? DateTimeOffset.UnixEpoch,
            ReadAt = readAt,
        });

        await db.SaveChangesAsync();
        return notificationId;
    }

    /// <summary>Stands in for an announcement this service has already consumed.</summary>
    public async Task RecordSaleAnnouncedAsync(Guid flightId)
    {
        await using var db = NewContext();

        db.SaleAnnouncements.Add(new SaleAnnouncement
        {
            FlightId = flightId,
            AnnouncedAt = DateTimeOffset.UnixEpoch,
        });

        await db.SaveChangesAsync();
    }

    public async Task<IReadOnlyList<Notification>> InboxAsync(Guid userId)
    {
        await using var db = NewContext();

        // Ordered after the read: SQLite cannot order on a DateTimeOffset column.
        var notifications = await db.Notifications
            .Where(notification => notification.UserId == userId)
            .ToListAsync();

        return [.. notifications.OrderBy(notification => notification.CreatedAt)];
    }

    public async Task<IReadOnlyList<Notification>> AllNotificationsAsync()
    {
        await using var db = NewContext();

        return await db.Notifications.ToListAsync();
    }

    public void Dispose() => _connection.Dispose();

    private static NotificationsDbContext ContextOn(SqliteConnection connection) =>
        new(new DbContextOptionsBuilder<NotificationsDbContext>().UseSqlite(connection).Options);
}
