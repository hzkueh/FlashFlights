using FlashFlights.Ordering.Domain;
using Microsoft.EntityFrameworkCore;

namespace FlashFlights.Ordering.Persistence;

/// <summary>
/// The one read that turns the append-only ledger into "each Seat's current
/// head" — its newest movement (ADR-0001). Every reader that derives a Seat's
/// status goes through here: the row-locked grant path, the expiry sweep, and
/// the browsing seat map. Keeping the newest-per-Seat rule in a single method is
/// what stops those three from disagreeing about which movement wins a tie.
/// </summary>
public static class SeatLedger
{
    public static async Task<Dictionary<Guid, SeatLedgerHead>> NewestMovementsAsync(
        OrderingDbContext db,
        IReadOnlyList<Guid> seatIds,
        CancellationToken cancellationToken)
    {
        var movements = await db.SeatMovements
            .Where(movement => seatIds.Contains(movement.SeatId))
            .Select(movement => new
            {
                movement.SeatId,
                movement.Type,
                movement.HoldId,
                movement.OccurredAt,
                movement.Id,
                ExpiresAt = movement.Hold!.ExpiresAt,
            })
            .ToListAsync(cancellationToken);

        // Newest movement per Seat decides its status; Id breaks a tie on the
        // timestamp so a Seat that gains two movements in the same instant resolves
        // deterministically. This ordering is the invariant callers rely on.
        return movements
            .GroupBy(movement => movement.SeatId)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var newest = group
                        .OrderByDescending(movement => movement.OccurredAt)
                        .ThenByDescending(movement => movement.Id)
                        .First();

                    return new SeatLedgerHead(newest.Type, newest.HoldId, newest.ExpiresAt);
                });
    }
}
