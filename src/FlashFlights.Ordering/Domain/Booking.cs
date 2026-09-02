namespace FlashFlights.Ordering.Domain;

/// <summary>
/// A Hold that resolved into Confirmed — the buyer's completed purchase
/// (CONTEXT.md). At most one per Hold, which is what makes "a resolved Hold
/// cannot be re-confirmed" a constraint the database enforces rather than a
/// rule the application hopes it remembered.
///
/// Its Seats are the Hold's Confirmed SeatMovements, for the same reason a
/// Hold has no seat list of its own.
/// </summary>
public sealed class Booking
{
    public Guid Id { get; set; }

    public Guid HoldId { get; set; }

    /// <summary>Denormalised from the Hold so booking history is a single-table read.</summary>
    public Guid UserId { get; set; }

    public DateTimeOffset ConfirmedAt { get; set; }

    /// <summary>Total taken by the simulated payment step — PricePerSeat times the Seats confirmed.</summary>
    public decimal PricePaid { get; set; }

    public Hold? Hold { get; set; }
}
