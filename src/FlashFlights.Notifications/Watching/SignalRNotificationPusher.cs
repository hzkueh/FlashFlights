using Microsoft.AspNetCore.SignalR;

namespace FlashFlights.Notifications.Watching;

/// <summary>
/// The narrow seam the dispatcher pushes through: hand it a User and their
/// Notification and it reaches that User's open connections, and no one else's.
/// The same shape <c>ISeatMapBroadcaster</c> gives the seat-map consumers, so the
/// dispatcher stays testable without a live connection.
/// </summary>
public interface INotificationPusher
{
    /// <summary>
    /// Pushes one Notification to its own User. Best-effort: the Notification is
    /// already persisted, so a User who is not connected — or a push that fails
    /// — still finds it in their inbox.
    /// </summary>
    Task NotificationCreatedAsync(
        Guid userId,
        NotificationView notification,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The one adapter that knows both <see cref="INotificationPusher"/> and SignalR
/// exist. Addresses the User rather than a group: SignalR fans the message out to
/// every connection that User has open and drops it when they have none, which is
/// precisely "pushed instantly if they are connected".
///
/// <para>
/// The id is written the same way on both sides of that lookup — plain
/// <see cref="Guid.ToString()"/> here, and in
/// <see cref="FlashFlightsUserIdProvider"/> for the connection. A mismatch would
/// be silent, since addressing a User nobody is signed in as is a no-op.
/// </para>
/// </summary>
public sealed class SignalRNotificationPusher(IHubContext<NotificationsHub> hub) : INotificationPusher
{
    public Task NotificationCreatedAsync(
        Guid userId,
        NotificationView notification,
        CancellationToken cancellationToken = default) =>
        hub.Clients
            .User(userId.ToString())
            .SendAsync(NotificationsHub.NotificationReceivedMethod, notification, cancellationToken);
}
