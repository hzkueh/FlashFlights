using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FlashFlights.Gateway.Identity;

/// <summary>
/// Used only by <c>dotnet ef</c>. The connection string here is never opened —
/// only the provider matters, because the provider decides the generated SQL.
/// </summary>
internal sealed class FlashFlightsIdentityDbContextDesignTimeFactory
    : IDesignTimeDbContextFactory<FlashFlightsIdentityDbContext>
{
    public FlashFlightsIdentityDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<FlashFlightsIdentityDbContext>()
            .UseSqlite("Data Source=identity.db")
            .Options);
}
