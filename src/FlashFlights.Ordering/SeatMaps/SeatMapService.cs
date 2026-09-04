using FlashFlights.Ordering.Domain;
using FlashFlights.Ordering.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FlashFlights.Ordering.SeatMaps;

/// <summary>
/// Reads one Flight's Seats and derives each Seat's status from its latest
/// movement. The derivation is <see cref="SeatStatusRules"/> — the same
/// function the locked grant path uses — so the map a buyer sees and the
/// decision a Hold makes can never disagree about what "held" or "available"
/// means, only about timing.
/// </summary>
public sealed class SeatMapService(OrderingDbContext db, TimeProvider clock) : ISeatMapService
{
    public async Task<FlightSeatMap> GetSeatMapAsync(Guid flightId, CancellationToken cancellationToken = default)
    {
        // Grid order, so the SPA renders 1A, 1B, … without sorting: RowNumber
        // then ColumnLetter is the order a buyer reads a cabin in.
        var seats = await db.Seats
            .Where(seat => seat.FlightId == flightId)
            .OrderBy(seat => seat.RowNumber)
            .ThenBy(seat => seat.ColumnLetter)
            .ToListAsync(cancellationToken);

        if (seats.Count == 0)
        {
            return new FlightSeatMap(flightId, []);
        }

        // The same newest-per-Seat read the locked grant path uses (ADR-0001), so
        // the map and a Hold decision can only ever differ on timing, never on
        // which movement is a Seat's latest.
        var latest = await SeatLedger.NewestMovementsAsync(db, [.. seats.Select(seat => seat.Id)], cancellationToken);
        var now = clock.GetUtcNow();

        var entries = seats
            .Select(seat => new SeatMapEntry(
                seat.Id,
                seat.SeatNumber,
                seat.RowNumber,
                seat.ColumnLetter,
                SeatStatusRules.StatusOf(StatusInputOn(latest, seat.Id), now)))
            .ToArray();

        return new FlightSeatMap(flightId, entries);
    }

    private static LatestMovement? StatusInputOn(IReadOnlyDictionary<Guid, SeatLedgerHead> latest, Guid seatId) =>
        latest.TryGetValue(seatId, out var head) ? head.ToStatusInput() : null;
}
