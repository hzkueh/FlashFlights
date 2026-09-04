namespace FlashFlights.Ordering.Holds;

/// <summary>
/// The heart of Ordering: granting Holds over the SeatMovement ledger under real
/// concurrency. Confirm and the expiry sweep join this interface in later slices
/// of ticket 05; this slice is the Hold that has to be provably safe against two
/// buyers racing for the same Seat before anything is built on top of it.
/// </summary>
public interface IHoldService
{
    /// <summary>
    /// Grants a Hold over the requested Seats, or explains why it could not.
    /// Appends the Held movements inside a single row-locked transaction so two
    /// concurrent calls for the same Seat cannot both win — the loser gets a
    /// <see cref="CreateHoldResult.Conflict"/>, never a partial grant.
    /// </summary>
    Task<CreateHoldResult> CreateHoldAsync(CreateHoldRequest request, CancellationToken cancellationToken = default);
}
