using FlashFlights.Ordering.Domain;
using FlashFlights.Ordering.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FlashFlights.Ordering.Tests;

/// <summary>
/// The SeatMovement ledger is append-only (ADR-0001): a Seat's status is
/// derived from its movements, so editing or deleting one silently rewrites
/// history that other reads have already been answered from.
///
/// These run against no database at all — the guard rejects the change before
/// any SQL is generated, which is the point: it is a rule about the
/// application's intent, not something the caller can slip past by choosing a
/// different provider.
/// </summary>
public class SeatMovementLedgerTests
{
    [Fact]
    public void Refuses_to_save_an_edited_movement()
    {
        using var db = BuildContext();
        db.Attach(AMovement()).State = EntityState.Modified;

        var error = Assert.Throws<InvalidOperationException>(() => db.SaveChanges());

        Assert.Contains("append-only", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Refuses_to_save_a_deleted_movement()
    {
        await using var db = BuildContext();
        db.Attach(AMovement()).State = EntityState.Deleted;

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());

        Assert.Contains("append-only", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The whole ledger is written by inserts, so the guard must not stand in their way.</summary>
    [Fact]
    public void Allows_a_new_movement_to_be_appended()
    {
        using var db = BuildContext();
        db.Add(AMovement());

        OrderingDbContext.EnsureLedgerIsAppendOnly(db.ChangeTracker);
    }

    /// <summary>A movement read back and left alone must not look like an edit.</summary>
    [Fact]
    public void Allows_saving_when_a_movement_is_only_being_read()
    {
        using var db = BuildContext();
        db.Attach(AMovement());

        OrderingDbContext.EnsureLedgerIsAppendOnly(db.ChangeTracker);
    }

    /// <summary>Naming the offender matters: the message is what a developer debugs from.</summary>
    [Fact]
    public void Names_the_seat_whose_movement_was_being_rewritten()
    {
        var movement = AMovement();

        using var db = BuildContext();
        db.Attach(movement).State = EntityState.Modified;

        var error = Assert.Throws<InvalidOperationException>(() => db.SaveChanges());

        Assert.Contains(movement.SeatId.ToString(), error.Message);
    }

    private static SeatMovement AMovement() => new()
    {
        Id = Guid.NewGuid(),
        SeatId = Guid.NewGuid(),
        HoldId = Guid.NewGuid(),
        Type = SeatMovementType.Held,
        OccurredAt = DateTimeOffset.UtcNow,
    };

    /// <summary>
    /// Configured for Npgsql — the provider Ordering really runs on — but never
    /// connected: these tests only exercise change tracking.
    /// </summary>
    private static OrderingDbContext BuildContext() =>
        new(new DbContextOptionsBuilder<OrderingDbContext>()
            .UseNpgsql("Host=unreachable.invalid;Database=none")
            .Options);
}
