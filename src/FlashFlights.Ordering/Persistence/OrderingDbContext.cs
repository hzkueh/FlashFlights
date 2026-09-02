using FlashFlights.Ordering.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace FlashFlights.Ordering.Persistence;

/// <summary>
/// Ordering's own store, and the only one on PostgreSQL: granting a Hold takes
/// real row-level locks over the SeatMovement ledger, which SQLite cannot
/// express (ADR-0001).
///
/// Note what is absent. No Seat status column, no Hold status column, no
/// Hold-to-Seat join table — all three are derivable from the ledger, and a
/// stored copy of a derived fact is the exact failure this design rules out.
/// </summary>
public sealed class OrderingDbContext(DbContextOptions<OrderingDbContext> options) : DbContext(options)
{
    public DbSet<Seat> Seats => Set<Seat>();

    public DbSet<SeatMovement> SeatMovements => Set<SeatMovement>();

    public DbSet<Hold> Holds => Set<Hold>();

    public DbSet<Booking> Bookings => Set<Booking>();

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        EnsureLedgerIsAppendOnly(ChangeTracker);

        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        EnsureLedgerIsAppendOnly(ChangeTracker);

        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>
    /// Rejects any attempt to edit or delete a SeatMovement. The ledger is the
    /// audit trail behind the no-double-hold guarantee, so a rewrite has to
    /// fail loudly at the point it is attempted rather than quietly succeed and
    /// leave a Seat's history disagreeing with what buyers were told.
    ///
    /// This is the first of two layers, and it catches the common case with a
    /// message that names the offender. It only sees work routed through
    /// SaveChanges, though — <c>ExecuteUpdate</c>, <c>ExecuteDelete</c>, and raw
    /// SQL bypass change tracking entirely — so a database trigger backs it up
    /// (migration <c>SeatMovementLedgerAppendOnly</c>).
    /// </summary>
    internal static void EnsureLedgerIsAppendOnly(ChangeTracker changeTracker)
    {
        var rewritten = changeTracker.Entries<SeatMovement>()
            .Where(entry => entry.State is EntityState.Modified or EntityState.Deleted)
            .ToArray();

        if (rewritten.Length == 0)
        {
            return;
        }

        var offenders = string.Join(
            ", ",
            rewritten.Select(entry => $"{entry.State} movement {entry.Entity.Id} on seat {entry.Entity.SeatId}"));

        throw new InvalidOperationException(
            $"The SeatMovement ledger is append-only; movements can only be inserted. Rejected: {offenders}.");
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Seat>(seat =>
        {
            seat.HasKey(s => s.Id);
            seat.Property(s => s.ColumnLetter).HasMaxLength(1).IsRequired();

            // SeatNumber is computed from RowNumber and ColumnLetter, so the
            // uniqueness of "12A on this flight" is a constraint on the pair.
            seat.HasIndex(s => new { s.FlightId, s.RowNumber, s.ColumnLetter }).IsUnique();
        });

        modelBuilder.Entity<SeatMovement>(movement =>
        {
            movement.HasKey(m => m.Id);
            movement.Property(m => m.Type).HasConversion<int>();

            // Deriving one Seat's status means reading its movements newest
            // first, and it happens inside the lock that grants every Hold —
            // the hottest read in the system.
            movement.HasIndex(m => new { m.SeatId, m.OccurredAt });

            movement.HasOne(m => m.Seat)
                .WithMany(s => s.Movements)
                .HasForeignKey(m => m.SeatId)
                .OnDelete(DeleteBehavior.Restrict);

            movement.HasOne(m => m.Hold)
                .WithMany(h => h.Movements)
                .HasForeignKey(m => m.HoldId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Hold>(hold =>
        {
            hold.HasKey(h => h.Id);
            hold.Property(h => h.PricePerSeat).HasPrecision(10, 2);

            // The expiry sweep's query: everything already past its TTL.
            hold.HasIndex(h => h.ExpiresAt);
            hold.HasIndex(h => h.UserId);
        });

        modelBuilder.Entity<Booking>(booking =>
        {
            booking.HasKey(b => b.Id);
            booking.Property(b => b.PricePaid).HasPrecision(10, 2);
            booking.HasIndex(b => b.UserId);

            // One Booking per Hold, enforced by the database: this is what
            // makes "a resolved Hold cannot be re-confirmed" true even if two
            // confirm requests race each other.
            booking.HasOne(b => b.Hold)
                .WithOne(h => h.Booking!)
                .HasForeignKey<Booking>(b => b.HoldId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
