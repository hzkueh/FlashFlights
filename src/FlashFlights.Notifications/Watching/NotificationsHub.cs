using System.Security.Claims;
using FlashFlights.ServiceDefaults.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace FlashFlights.Notifications.Watching;

/// <summary>
/// The SignalR hub a signed-in SPA holds open for its own alerts. Unlike the
/// seat-map hub — anonymous, and grouped by the Flight on screen — this one is
/// authenticated and addressed per User: an alert belongs to exactly one person,
/// and SignalR routes it by the connection's identity rather than by a group the
/// client asked to join. There is deliberately nothing to subscribe to here, so
/// a client cannot name whose notifications it would like.
///
/// <para>
/// Connecting is all a client does. A User with no connection misses nothing:
/// every alert is written to their inbox first, and the push is only how a
/// connected User hears it sooner (ticket 09, items 4 and 5).
/// </para>
/// </summary>
[Authorize]
public sealed class NotificationsHub : Hub
{
    /// <summary>The client method name a signed-in viewer handles to receive one alert.</summary>
    public const string NotificationReceivedMethod = "NotificationReceived";
}

/// <summary>
/// Tells SignalR which User a connection belongs to. Without this it reads
/// <see cref="ClaimTypes.NameIdentifier"/>, which a FlashFlights token does not
/// carry: claims arrive unmapped, so the User's id is the raw <c>sub</c>
/// (<see cref="FlashFlightsClaims.UserId"/>). Getting this wrong would not fail
/// loudly — every <c>Clients.User(...)</c> push would simply reach no one — so
/// the mapping is stated here once and asserted in the tests.
/// </summary>
public sealed class FlashFlightsUserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection) => UserIdFor(connection.User);

    /// <summary>
    /// The identity half on its own, so the one thing worth proving — that a
    /// FlashFlights token names the same User here as
    /// <see cref="SignalRNotificationPusher"/> addresses — can be proven without
    /// standing up a connection.
    /// </summary>
    public static string? UserIdFor(ClaimsPrincipal? user) => user?.GetUserId()?.ToString();
}
