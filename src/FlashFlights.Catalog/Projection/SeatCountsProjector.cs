using FlashFlights.Catalog.Domain;
using FlashFlights.Catalog.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FlashFlights.Catalog.Projection;

/// <summary>
/// Folds Ordering's seat-movement events into a Flight's <see cref="FlightSeatCounts"/>.
/// The one place the browse counts change — the consumers on the bus are thin
/// callers of the three methods here.
///
/// <para>
/// The projection moves Seats between buckets by count, never tracking which
/// Seat is where: a Held shifts <c>n</c> Available to Held, a Released shifts
/// them back, a Confirmed shifts Held to Confirmed. It deliberately holds no
/// per-Seat status — that would be the stored, mutable SeatStatus ADR-0001 rules
/// out, and a stale one at that. The authoritative per-Seat view is Ordering's
/// seat map, read live; these counts are advisory decoration for the list page.
/// </para>
///
/// <para>
/// <see cref="FlightSeatCounts.LastMovementAt"/> is the high-water mark that
/// makes applying an event idempotent: an event no newer than the last one
/// folded in is a redelivery or a straggler and is dropped, so the same
/// <c>SeatsHeld</c> arriving twice cannot double-count. It trusts Ordering's
/// clock for ordering only — the counts themselves come from the deltas.
/// </para>
/// </summary>
public sealed class SeatCountsProjector(CatalogDbContext db)
{
    public Task ApplyHeldAsync(Guid flightId, int seatCount, DateTimeOffset occurredAt, CancellationToken cancellationToken = default) =>
        ApplyAsync(flightId, occurredAt, cancellationToken, counts =>
        {
            counts.AvailableSeats = ClampToZero(counts.AvailableSeats - seatCount);
            counts.HeldSeats += seatCount;
        });

    public Task ApplyReleasedAsync(Guid flightId, int seatCount, DateTimeOffset occurredAt, CancellationToken cancellationToken = default) =>
        ApplyAsync(flightId, occurredAt, cancellationToken, counts =>
        {
            counts.HeldSeats = ClampToZero(counts.HeldSeats - seatCount);
            counts.AvailableSeats += seatCount;
        });

    public Task ApplyConfirmedAsync(Guid flightId, int seatCount, DateTimeOffset occurredAt, CancellationToken cancellationToken = default) =>
        ApplyAsync(flightId, occurredAt, cancellationToken, counts =>
        {
            counts.HeldSeats = ClampToZero(counts.HeldSeats - seatCount);
            counts.ConfirmedSeats += seatCount;
        });

    private async Task ApplyAsync(
        Guid flightId,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken,
        Action<FlightSeatCounts> move)
    {
        var counts = await db.FlightSeatCounts.FirstOrDefaultAsync(c => c.FlightId == flightId, cancellationToken);

        // No counts row means Catalog has not seeded this Flight's baseline yet
        // (TotalSeats and its all-Available starting point come from seeding, not
        // from events, since an event carries no total). Nothing sound to fold a
        // delta into, so drop it — the seed will establish the baseline, and any
        // event that predates the seed is one the buckets already reflect.
        if (counts is null)
        {
            return;
        }

        // Idempotent on Ordering's clock: an event no newer than what we have
        // already folded in is a redelivery or arrived out of order behind a
        // newer one. Dropping it is what stops a doubled SeatsHeld double-counting.
        //
        // The limit of a timestamp-only high-water mark, accepted deliberately:
        // two *distinct* operations on this Flight that commit in the same clock
        // tick share an OccurredAt, and the second is dropped here as if it were a
        // redelivery — a lost delta the counts do not later recover. That is
        // tolerable only because these counts are advisory (CONTEXT.md): the
        // authoritative, self-healing per-Seat view is Ordering's live seat map,
        // and no Hold is ever granted from this number. A message-id inbox would
        // close the gap but is disproportionate for a browse-only tally; revisit
        // if a later ticket makes these counts load-bearing.
        if (occurredAt <= counts.LastMovementAt)
        {
            return;
        }

        move(counts);
        counts.LastMovementAt = occurredAt;

        await db.SaveChangesAsync(cancellationToken);
    }

    // A lost or out-of-order event could otherwise drive a bucket negative, which
    // reads as nonsense on the list page. Under normal ordered delivery the
    // deltas keep Available + Held + Confirmed == Total on their own; this clamp
    // is only a guard against a bucket that would go below zero.
    private static int ClampToZero(int value) => value < 0 ? 0 : value;
}
