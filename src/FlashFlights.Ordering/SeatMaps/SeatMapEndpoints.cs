using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace FlashFlights.Ordering.SeatMaps;

/// <summary>
/// The HTTP surface for the live seat map, reached through the gateway at
/// <c>/api/ordering/seats?flightId=…</c>. Anonymous on purpose: browsing a seat
/// map needs no account (spec), and this is a read that grants nothing.
///
/// The wire shape stringifies <see cref="Domain.SeatStatus"/> rather than
/// leaking its numeric value, the same way a conflict names a Seat's blocking
/// status — the SPA branches on "Available" / "Held" / "Confirmed", not on 0/1/2.
/// </summary>
public static class SeatMapEndpoints
{
    public const string BasePath = "/seats";

    public static IEndpointRouteBuilder MapFlashFlightsSeatMaps(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(BasePath, GetSeatMapAsync);

        return endpoints;
    }

    /// <summary>One Seat on the wire, with its status as a name the SPA reads.</summary>
    public sealed record SeatOnMap(Guid SeatId, string SeatNumber, int Row, string Column, string Status);

    /// <summary>A Flight's whole seat map on the wire.</summary>
    public sealed record SeatMapResponse(Guid FlightId, IReadOnlyList<SeatOnMap> Seats);

    private static async Task<Ok<SeatMapResponse>> GetSeatMapAsync(
        Guid flightId,
        ISeatMapService seatMaps,
        CancellationToken cancellationToken)
    {
        var map = await seatMaps.GetSeatMapAsync(flightId, cancellationToken);

        var seats = map.Seats
            .Select(seat => new SeatOnMap(
                seat.SeatId,
                seat.SeatNumber,
                seat.RowNumber,
                seat.ColumnLetter,
                seat.Status.ToString()))
            .ToArray();

        return TypedResults.Ok(new SeatMapResponse(map.FlightId, seats));
    }
}
