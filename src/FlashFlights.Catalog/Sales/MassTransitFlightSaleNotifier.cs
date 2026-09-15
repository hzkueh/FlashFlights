using FlashFlights.Contracts;
using MassTransit;

namespace FlashFlights.Catalog.Sales;

/// <summary>
/// Publishes <see cref="FlightSaleStarted"/> onto the bus for Notifications to
/// consume. The one adapter that knows both <see cref="IFlightSaleNotifier"/>
/// and MassTransit exist; everything above it speaks only the narrow interface.
///
/// Deliberately thin, and deliberately not error-swallowing: a publish that
/// fails propagates so the announcer leaves the Flight unannounced and tries
/// again next tick (see <see cref="IFlightSaleNotifier"/>).
/// </summary>
public sealed class MassTransitFlightSaleNotifier(IPublishEndpoint publish) : IFlightSaleNotifier
{
    public Task SaleStartedAsync(FlightSaleStarted announcement, CancellationToken cancellationToken = default) =>
        publish.Publish(announcement, cancellationToken);
}
