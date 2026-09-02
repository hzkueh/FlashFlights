using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FlashFlights.Notifications.Persistence;

/// <summary>
/// Used only by <c>dotnet ef</c>. The connection string here is never opened —
/// only the provider matters, because the provider decides the generated SQL.
/// </summary>
internal sealed class NotificationsDbContextDesignTimeFactory
    : IDesignTimeDbContextFactory<NotificationsDbContext>
{
    public NotificationsDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<NotificationsDbContext>()
            .UseSqlite("Data Source=notifications.db")
            .Options);
}
