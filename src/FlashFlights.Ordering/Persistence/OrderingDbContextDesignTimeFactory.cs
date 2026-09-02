using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FlashFlights.Ordering.Persistence;

/// <summary>
/// Used only by <c>dotnet ef</c>. The connection string here is never opened —
/// only the provider matters, because the provider decides the generated SQL.
/// </summary>
internal sealed class OrderingDbContextDesignTimeFactory : IDesignTimeDbContextFactory<OrderingDbContext>
{
    public OrderingDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<OrderingDbContext>()
            .UseNpgsql("Host=localhost;Database=flashflights_ordering")
            .Options);
}
