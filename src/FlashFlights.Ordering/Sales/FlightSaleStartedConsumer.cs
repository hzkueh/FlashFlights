using FlashFlights.Contracts;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace FlashFlights.Ordering.Sales;

/// <summary>
/// The bus-facing edge of the sale-window rule: takes Catalog's announcement that
/// a sale has opened and hands it to the recorder. Thin on purpose, like the
/// seat-movement consumers — the rule it feeds lives in
/// <see cref="Ordering.Domain.SaleWindowRules"/>, and what to remember lives in
/// <see cref="ISaleWindowRecorder"/>.
///
/// <para>
/// This is the second consumer of <see cref="FlightSaleStarted"/>; Notifications
/// has its own. Neither steals the other's copy because the shared bus wiring
/// prefixes every queue with its service name.
/// </para>
///
/// <para>
/// A failure here is <em>not</em> swallowed. Catalog announces a Flight once, and
/// until this row is written Ordering refuses every Hold on that Flight
/// (ADR-0003) — so a quietly dropped announcement is a sale that never becomes
/// buyable. Letting the exception reach MassTransit is what gets the message
/// retried and, failing that, moved to the error queue where it can be seen. The
/// recorder is idempotent, so a retry records nothing twice.
/// </para>
/// </summary>
public sealed class FlightSaleStartedConsumer(
    ISaleWindowRecorder recorder,
    ILogger<FlightSaleStartedConsumer> logger) : IConsumer<FlightSaleStarted>
{
    public async Task Consume(ConsumeContext<FlightSaleStarted> context)
    {
        await recorder.OnFlightSaleStartedAsync(context.Message, context.CancellationToken);

        logger.LogInformation(
            "Flight {FlightNumber} ({FlightId}) is on sale until {SaleEndsAt}; holds are open for it.",
            context.Message.FlightNumber,
            context.Message.FlightId,
            context.Message.SaleEndsAt);
    }
}
