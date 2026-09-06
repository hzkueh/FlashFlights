using Microsoft.AspNetCore.SignalR;

namespace FlashFlights.Notifications.SeatMaps;

/// <summary>
/// The SignalR hub the SPA holds open while a seat map is on screen. A viewer
/// <see cref="Subscribe"/>s to the one Flight it is looking at and is pushed
/// changes for that Flight alone — never for a Flight it isn't watching — by
/// living in that Flight's group. Leaving the page drops the connection, and
/// SignalR retires the group membership with it; the client holds one connection
/// per Flight, so there is no live connection that needs to switch groups.
///
/// Anonymous, like the seat map read it decorates: anyone may watch a cabin fill
/// up, and doing so reserves nothing. The push is advisory — Ordering's locked
/// grant (ADR-0001) remains the sole authority on whether a Seat is takeable, so
/// a viewer subscribed here can never <em>do</em> anything a stale map misleads
/// them into; at worst a cell repaints a moment late.
/// </summary>
public sealed class SeatMapHub : Hub
{
    /// <summary>The group every viewer of one Flight shares. One membership, one Flight.</summary>
    public static string GroupFor(Guid flightId) => $"flight-{flightId:D}";

    /// <summary>The client method name a viewer handles to repaint changed Seats.</summary>
    public const string SeatsChangedMethod = "SeatsChanged";

    /// <summary>
    /// Joins this connection to a Flight's group so it starts receiving that
    /// Flight's changes. Idempotent — re-subscribing (e.g. after the SPA
    /// re-renders) rejoins the same group harmlessly.
    /// </summary>
    public Task Subscribe(Guid flightId) =>
        Groups.AddToGroupAsync(Context.ConnectionId, GroupFor(flightId), Context.ConnectionAborted);
}
