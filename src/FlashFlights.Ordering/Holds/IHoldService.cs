namespace FlashFlights.Ordering.Holds;

/// <summary>
/// The heart of Ordering: granting and confirming Holds over the SeatMovement
/// ledger under real concurrency. The expiry sweep joins this interface in the
/// final slice of ticket 05; grant and confirm are the two writes that have to
/// be provably safe against two buyers racing for the same Seat.
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

    /// <summary>
    /// Confirms a live Hold into a Booking, appending the Confirmed movements and
    /// taking the frozen FlashPrice through a simulated payment that always
    /// succeeds instantly. Only the Hold's owner may confirm it. Runs inside the
    /// same row lock <see cref="CreateHoldAsync"/> uses, so a second confirm loses
    /// cleanly (<see cref="ConfirmHoldResult.AlreadyConfirmed"/>) and a Hold past
    /// its TTL is refused (<see cref="ConfirmHoldResult.Expired"/>) on the very
    /// boundary the read side treats it as expired.
    /// </summary>
    /// <param name="holdId">The Hold to confirm.</param>
    /// <param name="userId">The caller, taken from the token — a buyer may only confirm their own Hold.</param>
    Task<ConfirmHoldResult> ConfirmHoldAsync(Guid holdId, Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Posts the compensating <c>Released</c> movements for every Hold that has
    /// passed its TTL without being confirmed. The read side already computes such
    /// a Hold back to Available without this having run (ADR-0001); the sweep
    /// exists so that a <em>durable</em> movement records the expiry — it is what
    /// lets Catalog's SeatCounts learn of a silent expiry, so the sweep's interval
    /// bounds how long the list page and a flight's seat map may disagree. Idempotent
    /// and safe to run against a live system: it takes the same row lock as
    /// <see cref="CreateHoldAsync"/> and refuses on the same boundary as confirm,
    /// so it never releases Seats a confirm could still win.
    /// </summary>
    /// <returns>How many Holds were expired and how many Seats released this run.</returns>
    Task<ExpireHoldsResult> ExpireHoldsAsync(CancellationToken cancellationToken = default);
}
