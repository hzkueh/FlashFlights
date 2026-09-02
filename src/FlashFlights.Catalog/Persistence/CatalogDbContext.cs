using FlashFlights.Catalog.Domain;
using Microsoft.EntityFrameworkCore;

namespace FlashFlights.Catalog.Persistence;

/// <summary>
/// Catalog's own store. SQLite: browsing carries no row-locking requirement,
/// so only Ordering needs PostgreSQL (ADR-0001).
/// </summary>
public sealed class CatalogDbContext(DbContextOptions<CatalogDbContext> options) : DbContext(options)
{
    public DbSet<Flight> Flights => Set<Flight>();

    public DbSet<FlightSeatCounts> FlightSeatCounts => Set<FlightSeatCounts>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Flight>(flight =>
        {
            flight.HasKey(f => f.Id);
            flight.Property(f => f.FlightNumber).HasMaxLength(8).IsRequired();
            flight.Property(f => f.Origin).HasMaxLength(3).IsRequired();
            flight.Property(f => f.Destination).HasMaxLength(3).IsRequired();
            flight.Property(f => f.FlashPrice).HasPrecision(10, 2);

            // The list page's default ordering: what is on sale, soonest first.
            flight.HasIndex(f => f.SaleStartsAt);

            flight.HasOne(f => f.SeatCounts)
                .WithOne(counts => counts.Flight!)
                .HasForeignKey<FlightSeatCounts>(counts => counts.FlightId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<FlightSeatCounts>(counts =>
        {
            counts.HasKey(c => c.FlightId);
            counts.Property(c => c.FlightId).ValueGeneratedNever();
        });
    }
}
