using FlashFlights.Ordering.Domain;
using FlashFlights.Ordering.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace FlashFlights.Ordering.Tests;

/// <summary>
/// Guards the shape of the ledger schema itself. What is <em>absent</em> is the
/// design (ADR-0001): a stored SeatStatus, a stored Hold status, or a
/// Hold-to-Seat join table would each be a second copy of something the ledger
/// already answers, and the first one to disagree would hand two buyers the
/// same Seat.
///
/// These read the model rather than a database, so they run against the real
/// Npgsql provider without needing a container.
/// </summary>
public class OrderingSchemaTests
{
    [Fact]
    public void Has_exactly_the_four_ledger_tables()
    {
        Assert.Equal(
            ["Bookings", "Holds", "SeatMovements", "Seats"],
            Model().GetEntityTypes().Select(type => type.GetTableName()!).Order().ToArray());
    }

    /// <summary>
    /// A Seat is a fixed position and nothing else. Its status comes from its
    /// movements, so any column beyond these four is a status column wearing a
    /// different name.
    /// </summary>
    [Fact]
    public void Seat_stores_its_position_and_no_status()
    {
        Assert.Equal(
            ["ColumnLetter", "FlightId", "Id", "RowNumber"],
            MappedPropertyNames<Seat>());
    }

    /// <summary>
    /// Whether a Hold is live, confirmed, or expired is computed from ExpiresAt
    /// and whether a resolving movement exists. Storing it would let the sweep
    /// and a concurrent confirm disagree about the same Hold.
    /// </summary>
    [Fact]
    public void Hold_stores_its_terms_and_no_status()
    {
        Assert.Equal(
            ["CreatedAt", "ExpiresAt", "FlightId", "Id", "PricePerSeat", "UserId"],
            MappedPropertyNames<Hold>());
    }

    /// <summary>
    /// A Hold's Seats are its Held movements. A join table would be a second
    /// list of the same thing, free to drift from the ledger.
    /// </summary>
    [Fact]
    public void Hold_has_no_seat_join_table()
    {
        var holdReferences = Model().GetEntityTypes()
            .SelectMany(type => type.GetForeignKeys())
            .Where(key => key.PrincipalEntityType.ClrType == typeof(Hold))
            .Select(key => key.DeclaringEntityType.ClrType)
            .ToArray();

        Assert.Equal([typeof(Booking), typeof(SeatMovement)], holdReferences.Order(ByTypeName).ToArray());
    }

    /// <summary>Derived from RowNumber and ColumnLetter, so it must not also be a column.</summary>
    [Fact]
    public void Seat_number_is_computed_rather_than_stored()
    {
        Assert.Null(Model().FindEntityType(typeof(Seat))!.FindProperty(nameof(Seat.SeatNumber)));
    }

    /// <summary>
    /// The database, not the application, is what makes "a resolved Hold cannot
    /// be re-confirmed" hold when two confirm requests race.
    /// </summary>
    [Fact]
    public void At_most_one_booking_can_exist_per_hold()
    {
        var holdId = Model().FindEntityType(typeof(Booking))!.FindProperty(nameof(Booking.HoldId))!;

        Assert.Contains(
            holdId.GetContainingIndexes(),
            index => index.IsUnique && index.Properties.Count == 1);
    }

    /// <summary>Every movement traces to the Hold that caused it — there are no orphan entries.</summary>
    [Fact]
    public void Every_movement_belongs_to_a_seat_and_a_hold()
    {
        var movement = Model().FindEntityType(typeof(SeatMovement))!;

        Assert.False(movement.FindProperty(nameof(SeatMovement.SeatId))!.IsNullable);
        Assert.False(movement.FindProperty(nameof(SeatMovement.HoldId))!.IsNullable);
    }

    private static Comparer<Type> ByTypeName =>
        Comparer<Type>.Create((left, right) => string.CompareOrdinal(left.Name, right.Name));

    private static string[] MappedPropertyNames<TEntity>() =>
        [.. Model().FindEntityType(typeof(TEntity))!.GetProperties().Select(property => property.Name).Order()];

    private static IModel Model()
    {
        using var db = new OrderingDbContext(new DbContextOptionsBuilder<OrderingDbContext>()
            .UseNpgsql("Host=unreachable.invalid;Database=none")
            .Options);

        return db.Model;
    }
}
