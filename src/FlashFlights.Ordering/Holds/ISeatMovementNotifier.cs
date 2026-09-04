namespace FlashFlights.Ordering.Holds;

/// <summary>
/// How <see cref="HoldService"/> tells the rest of the system that Seats
/// changed hands, without knowing there is a bus. Each committed operation —
/// a granted Hold, a confirm, a sweep's release — announces itself here once,
/// carrying the Seats it touched and the clock instant it touched them.
///
/// <para>
/// A narrow seam on purpose. The guarantee HoldService exists to make lives in
/// its transaction and its row lock; publishing is a downstream courtesy for
/// Catalog's browse counts (ADR-0001). Keeping it behind this interface means
/// the concurrency tests construct a HoldService with a no-op notifier and
/// never stand up a broker, and a bus-less unit test can still assert that the
/// right announcement was made.
/// </para>
///
/// <para>
/// Called after the transaction commits, so a Seat is only announced once its
/// movement is durable. A failure to announce leaves the ledger correct and the
/// browse counts briefly behind — the advisory trade CONTEXT.md's SeatCounts
/// entry describes — never the other way around.
/// </para>
/// </summary>
public interface ISeatMovementNotifier
{
    Task SeatsHeldAsync(
        Guid flightId,
        IReadOnlyList<Guid> seatIds,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken = default);

    Task SeatsReleasedAsync(
        Guid flightId,
        IReadOnlyList<Guid> seatIds,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken = default);

    Task SeatsConfirmedAsync(
        Guid flightId,
        IReadOnlyList<Guid> seatIds,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Announces nothing. The default for tests and any host that runs Ordering
/// without a bus — the Hold logic is complete without it, since the browse
/// counts it feeds are advisory (ADR-0001).
/// </summary>
public sealed class NullSeatMovementNotifier : ISeatMovementNotifier
{
    public Task SeatsHeldAsync(Guid flightId, IReadOnlyList<Guid> seatIds, DateTimeOffset occurredAt, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task SeatsReleasedAsync(Guid flightId, IReadOnlyList<Guid> seatIds, DateTimeOffset occurredAt, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task SeatsConfirmedAsync(Guid flightId, IReadOnlyList<Guid> seatIds, DateTimeOffset occurredAt, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
