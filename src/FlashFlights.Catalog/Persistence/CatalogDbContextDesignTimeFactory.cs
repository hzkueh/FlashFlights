using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FlashFlights.Catalog.Persistence;

/// <summary>
/// Used only by <c>dotnet ef</c>. Without it the tooling has to build the real
/// host, which starts connecting to RabbitMQ just to scaffold a migration. The
/// connection string here is never opened — only the provider matters, because
/// the provider is what decides the generated SQL.
/// </summary>
internal sealed class CatalogDbContextDesignTimeFactory : IDesignTimeDbContextFactory<CatalogDbContext>
{
    public CatalogDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<CatalogDbContext>()
            .UseSqlite("Data Source=catalog.db")
            .Options);
}
