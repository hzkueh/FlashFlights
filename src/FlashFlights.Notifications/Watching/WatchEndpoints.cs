using System.Security.Claims;
using FlashFlights.ServiceDefaults.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace FlashFlights.Notifications.Watching;

/// <summary>
/// The HTTP surface for Watches and the Notification inbox, reached through the
/// gateway at <c>/api/notifications/watches</c> and
/// <c>/api/notifications/inbox</c> (the gateway strips
/// <c>/api/notifications</c>).
///
/// <para>
/// Every route here is authenticated, and every one is scoped to whoever the
/// token says — never a body or query field. A Watch and an inbox belong to one
/// person, so there is deliberately no way to name a User from outside, the same
/// rule Booking history follows.
/// </para>
/// </summary>
public static class WatchEndpoints
{
    public const string WatchesPath = "/watches";

    public const string InboxPath = "/inbox";

    public static IEndpointRouteBuilder MapFlashFlightsWatches(this IEndpointRouteBuilder endpoints)
    {
        var watches = endpoints.MapGroup(WatchesPath).RequireAuthorization();

        watches.MapGet("/", ListWatchesAsync);
        watches.MapPut("/{flightId:guid}", WatchAsync);
        watches.MapDelete("/{flightId:guid}", UnwatchAsync);

        var inbox = endpoints.MapGroup(InboxPath).RequireAuthorization();

        inbox.MapGet("/", ReadInboxAsync);
        inbox.MapPost("/{notificationId:guid}/read", MarkReadAsync);

        return endpoints;
    }

    /// <summary>The Flights this User is watching — ids alone; Catalog owns the rest.</summary>
    public sealed record WatchesResponse(IReadOnlyList<Guid> FlightIds);

    private static async Task<Ok<WatchesResponse>> ListWatchesAsync(
        ClaimsPrincipal principal,
        IWatchService watches,
        CancellationToken cancellationToken)
    {
        var flightIds = await watches.ListWatchedFlightIdsAsync(principal.RequireUserId(), cancellationToken);

        return TypedResults.Ok(new WatchesResponse(flightIds));
    }

    /// <summary>
    /// PUT rather than POST: watching is a state the caller is asserting, not an
    /// event to append, so sending it twice leaves the same single subscription
    /// and answers the same way. A sale that has already opened is a conflict —
    /// a Watch fires only on the window opening, and that moment has passed.
    /// </summary>
    private static async Task<Results<NoContent, Conflict<string>>> WatchAsync(
        Guid flightId,
        ClaimsPrincipal principal,
        IWatchService watches,
        CancellationToken cancellationToken)
    {
        var outcome = await watches.WatchAsync(principal.RequireUserId(), flightId, cancellationToken);

        return outcome == WatchOutcome.SaleAlreadyStarted
            ? TypedResults.Conflict("This flight's sale has already started, so there is nothing left to watch for.")
            : TypedResults.NoContent();
    }

    private static async Task<NoContent> UnwatchAsync(
        Guid flightId,
        ClaimsPrincipal principal,
        IWatchService watches,
        CancellationToken cancellationToken)
    {
        await watches.UnwatchAsync(principal.RequireUserId(), flightId, cancellationToken);

        return TypedResults.NoContent();
    }

    private static async Task<Ok<InboxView>> ReadInboxAsync(
        ClaimsPrincipal principal,
        INotificationInbox inbox,
        CancellationToken cancellationToken)
    {
        var view = await inbox.ListAsync(principal.RequireUserId(), cancellationToken);

        return TypedResults.Ok(view);
    }

    /// <summary>
    /// A Notification that is not this User's answers 404, not 403: the inbox is
    /// scoped to the token, so from out here someone else's alert simply does not
    /// exist.
    /// </summary>
    private static async Task<Results<NoContent, NotFound>> MarkReadAsync(
        Guid notificationId,
        ClaimsPrincipal principal,
        INotificationInbox inbox,
        CancellationToken cancellationToken)
    {
        var marked = await inbox.MarkReadAsync(principal.RequireUserId(), notificationId, cancellationToken);

        return marked ? TypedResults.NoContent() : TypedResults.NotFound();
    }
}
