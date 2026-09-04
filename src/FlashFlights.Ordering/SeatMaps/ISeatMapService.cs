namespace FlashFlights.Ordering.SeatMaps;

/// <summary>
/// The read side of the ledger for browsing: every Seat on a Flight with its
/// current <see cref="Domain.SeatStatus"/>, computed the same way granting a
/// Hold computes it (<see cref="Domain.SeatStatusRules"/>). Unauthenticated —
/// anyone may look at a seat map, signed in or not (spec).
///
/// This is a plain read, not the row-locked transaction that grants a Hold: it
/// makes no promises about the instant after it returns, only reports what the
/// ledger says now. A Seat it shows Available can be taken a moment later, which
/// is exactly why holding one still has to go through the locked path — the map
/// is what a buyer looks at, never what decides who wins a Seat.
/// </summary>
public interface ISeatMapService
{
    Task<FlightSeatMap> GetSeatMapAsync(Guid flightId, CancellationToken cancellationToken = default);
}
