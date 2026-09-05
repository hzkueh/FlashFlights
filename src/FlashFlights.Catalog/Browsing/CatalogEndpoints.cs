using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace FlashFlights.Catalog.Browsing;

/// <summary>
/// The HTTP surface for browsing, reached through the gateway at
/// <c>/api/catalog/flights</c> (the gateway strips <c>/api/catalog</c>). Both
/// routes are anonymous: a visitor browses the catalog and any flight's details
/// without signing in (spec).
///
/// The wire shape names the <see cref="SaleState"/> ("Upcoming" / "Live" /
/// "Ended") rather than leaking its numeric value, the same way the seat map
/// names a Seat's status — the SPA branches on the name.
/// </summary>
public static class CatalogEndpoints
{
    public const string BasePath = "/flights";

    public static IEndpointRouteBuilder MapFlashFlightsCatalog(this IEndpointRouteBuilder endpoints)
    {
        var flights = endpoints.MapGroup(BasePath);

        flights.MapGet("/", ListFlightsAsync);
        flights.MapGet("/{flightId:guid}", GetFlightAsync);

        return endpoints;
    }

    /// <summary>The advisory seat tally on the wire; absent when Catalog has no projection row yet.</summary>
    public sealed record SeatCountsResponse(int Total, int Available, int Held, int Confirmed);

    /// <summary>One Flight on the wire, with its sale state as a name and the raw window for the SPA's countdown.</summary>
    public sealed record FlightResponse(
        Guid Id,
        string FlightNumber,
        string Origin,
        string Destination,
        DateTimeOffset DepartureAt,
        decimal FlashPrice,
        decimal? ReferenceFare,
        DateTimeOffset SaleStartsAt,
        DateTimeOffset SaleEndsAt,
        string SaleState,
        SeatCountsResponse? SeatCounts);

    private static async Task<Ok<IReadOnlyList<FlightResponse>>> ListFlightsAsync(
        IFlightCatalogService catalog,
        CancellationToken cancellationToken)
    {
        var flights = await catalog.ListFlightsAsync(cancellationToken);

        return TypedResults.Ok<IReadOnlyList<FlightResponse>>([.. flights.Select(ToResponse)]);
    }

    private static async Task<Results<Ok<FlightResponse>, NotFound>> GetFlightAsync(
        Guid flightId,
        IFlightCatalogService catalog,
        CancellationToken cancellationToken)
    {
        var flight = await catalog.GetFlightAsync(flightId, cancellationToken);

        return flight is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(ToResponse(flight));
    }

    private static FlightResponse ToResponse(FlightView flight) =>
        new(
            flight.Id,
            flight.FlightNumber,
            flight.Origin,
            flight.Destination,
            flight.DepartureAt,
            flight.FlashPrice,
            flight.ReferenceFare,
            flight.SaleStartsAt,
            flight.SaleEndsAt,
            flight.SaleState.ToString(),
            flight.SeatCounts is { } counts
                ? new SeatCountsResponse(counts.Total, counts.Available, counts.Held, counts.Confirmed)
                : null);
}
