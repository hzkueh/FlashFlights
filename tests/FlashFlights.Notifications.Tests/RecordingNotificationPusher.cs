using System.Collections.Concurrent;
using FlashFlights.Notifications.Watching;

namespace FlashFlights.Notifications.Tests;

/// <summary>
/// Stands in for the SignalR push, recording who was addressed and with what —
/// and, when asked, failing every push, which is how the tests check that a
/// Notification survives a push that never lands.
/// </summary>
internal sealed class RecordingNotificationPusher : INotificationPusher
{
    private readonly ConcurrentQueue<(Guid UserId, NotificationView Notification)> _pushes = new();

    public bool FailEveryPush { get; init; }

    public IReadOnlyCollection<(Guid UserId, NotificationView Notification)> Pushes => _pushes;

    public Task NotificationCreatedAsync(
        Guid userId,
        NotificationView notification,
        CancellationToken cancellationToken = default)
    {
        if (FailEveryPush)
        {
            return Task.FromException(new InvalidOperationException("No hub here."));
        }

        _pushes.Enqueue((userId, notification));
        return Task.CompletedTask;
    }
}
