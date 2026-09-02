namespace FlashFlights.Ordering.Domain;

/// <summary>
/// A buyer's in-progress reservation over specific Seats, created by posting
/// Held SeatMovements for them (CONTEXT.md). That is why there is no
/// Hold-to-Seat join table: a Hold's Seats <em>are</em> its Held movements,
/// and a second list of them could only ever drift out of agreement with the
/// ledger.
///
/// Carries no status column either. Whether a Hold is live, confirmed, or
/// expired is computed from <see cref="ExpiresAt"/> and whether a resolving
/// movement exists — an unresolved Hold past its TTL reads as expired without
/// waiting for the sweep to have run (ADR-0001).
/// </summary>
public sealed class Hold
{
    public Guid Id { get; set; }

    /// <summary>Catalog owns Flights; this is a cross-service reference, not an FK.</summary>
    public Guid FlightId { get; set; }

    /// <summary>The Identity store owns Users; this is a cross-service reference, not an FK.</summary>
    public Guid UserId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>End of the TTL, stamped at creation so the read side and the sweep read one value rather than each recomputing it.</summary>
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>
    /// The FlashPrice per Seat as it stood when the Hold was granted, so a
    /// later price change cannot alter what the buyer was shown and agreed to.
    /// </summary>
    public decimal PricePerSeat { get; set; }

    public ICollection<SeatMovement> Movements { get; set; } = [];

    public Booking? Booking { get; set; }
}
