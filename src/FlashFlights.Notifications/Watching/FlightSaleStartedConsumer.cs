using FlashFlights.Contracts;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace FlashFlights.Notifications.Watching;

/// <summary>
/// The bus-facing edge of the watch feature: takes Catalog's announcement that a
/// sale has opened and hands it to the dispatcher. Thin on purpose, like the
/// seat-map relay consumers — every decision about who is told lives in
/// <see cref="IWatchNotificationDispatcher"/>.
///
/// <para>
/// A failure here is <em>not</em> swallowed. Catalog announces a Flight once, so
/// the alert has no second source: letting the exception reach MassTransit is
/// what gets the message retried and, failing that, moved to the error queue
/// where it can be seen — rather than the whole watch list silently missing a
/// sale. The dispatcher is idempotent, so a retry re-notifies no one.
/// </para>
/// </summary>
public sealed class FlightSaleStartedConsumer(
    IWatchNotificationDispatcher dispatcher,
    ILogger<FlightSaleStartedConsumer> logger) : IConsumer<FlightSaleStarted>
{
    public async Task Consume(ConsumeContext<FlightSaleStarted> context)
    {
        var created = await dispatcher.OnFlightSaleStartedAsync(context.Message, context.CancellationToken);

        logger.LogInformation(
            "Flight {FlightNumber} ({FlightId}) went on sale; notified {NotifiedCount} watcher(s).",
            context.Message.FlightNumber,
            context.Message.FlightId,
            created);
    }
}
