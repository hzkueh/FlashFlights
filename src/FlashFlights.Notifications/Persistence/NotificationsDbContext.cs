using FlashFlights.Notifications.Domain;
using Microsoft.EntityFrameworkCore;

namespace FlashFlights.Notifications.Persistence;

/// <summary>
/// Notifications' own store. SQLite: nothing here needs row-level locking, so
/// only Ordering runs on PostgreSQL (ADR-0001).
/// </summary>
public sealed class NotificationsDbContext(DbContextOptions<NotificationsDbContext> options) : DbContext(options)
{
    public DbSet<Watch> Watches => Set<Watch>();

    public DbSet<Notification> Notifications => Set<Notification>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Watch>(watch =>
        {
            watch.HasKey(w => w.Id);

            // Watching twice is the same subscription, not two of them — this
            // makes the Watch endpoint idempotent at the store rather than
            // relying on every caller to check first.
            watch.HasIndex(w => new { w.UserId, w.FlightId }).IsUnique();
        });

        modelBuilder.Entity<Notification>(notification =>
        {
            notification.HasKey(n => n.Id);
            notification.Property(n => n.Body).HasMaxLength(200).IsRequired();

            // The inbox query: this User's notifications, newest first.
            notification.HasIndex(n => new { n.UserId, n.CreatedAt });

            // A Flight's sale goes live exactly once, so one notification per
            // (User, Flight) is the whole truth — and a redelivered
            // FlightSaleStarted cannot duplicate anyone's inbox. Widen this if
            // a second Notification trigger is ever added.
            notification.HasIndex(n => new { n.UserId, n.FlightId }).IsUnique();
        });
    }
}
