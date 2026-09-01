using FlashFlights.Contracts;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace FlashFlights.ServiceDefaults.Wiring;

/// <summary>
/// Consumes the ticket-01 wiring probe. Delete with <see cref="PingSent"/>
/// once the real event contracts land.
/// </summary>
public sealed class PingSentConsumer(IPingLog log, ILogger<PingSentConsumer> logger) : IConsumer<PingSent>
{
    public Task Consume(ConsumeContext<PingSent> context)
    {
        log.Record(context.Message);
        logger.LogInformation(
            "Consumed PingSent {PingId} published by {Source}",
            context.Message.PingId,
            context.Message.Source);

        return Task.CompletedTask;
    }
}
