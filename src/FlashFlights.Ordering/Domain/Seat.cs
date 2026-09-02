namespace FlashFlights.Ordering.Domain;

/// <summary>
/// One uniquely identified position in a Flight's flash-sale allocation, e.g.
/// 12A. Fixed once seeded — buying a Seat never creates or removes one.
///
/// Deliberately carries no status column. A Seat's SeatStatus is computed from
/// its <see cref="Movements"/> (ADR-0001); a stored status is the mutable
/// field this whole design exists to avoid.
/// </summary>
public sealed class Seat
{
    public Guid Id { get; set; }

    /// <summary>Catalog owns Flights; this is a cross-service reference, not an FK.</summary>
    public Guid FlightId { get; set; }

    public int RowNumber { get; set; }

    /// <summary>The letter within the row, e.g. "A".</summary>
    public required string ColumnLetter { get; set; }

    /// <summary>
    /// What a buyer calls this Seat, e.g. "12A" — the attribute CONTEXT.md
    /// sanctions under this name. Computed rather than stored: it is two
    /// existing columns concatenated, and a stored copy is one more thing that
    /// can disagree with them.
    /// </summary>
    public string SeatNumber => $"{RowNumber}{ColumnLetter}";

    public ICollection<SeatMovement> Movements { get; set; } = [];
}
