using Microsoft.AspNetCore.SignalR;

namespace FlashFlights.Notifications.SeatMaps;

/// <summary>
/// The one adapter that knows both <see cref="ISeatMapBroadcaster"/> and SignalR
/// exist. Sends each Flight's changes to that Flight's group alone (<see
/// cref="SeatMapHub.GroupFor"/>), so a viewer subscribed to one Flight is never
/// handed another's — the per-Flight scoping the consumers rely on lives here and
/// in the hub's group membership, nowhere else.
///
/// The whole message is one argument the client repaints from, so the method
/// name and payload shape are the contract with the SPA; <see
/// cref="SeatMapHub.SeatsChangedMethod"/> names it on both sides.
/// </summary>
public sealed class SignalRSeatMapBroadcaster(IHubContext<SeatMapHub> hub) : ISeatMapBroadcaster
{
    public Task SeatsChangedAsync(
        Guid flightId,
        IReadOnlyList<SeatChange> seats,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken = default) =>
        hub.Clients
            .Group(SeatMapHub.GroupFor(flightId))
            .SendAsync(
                SeatMapHub.SeatsChangedMethod,
                new SeatMapChanged(flightId, seats, occurredAt),
                cancellationToken);
}
