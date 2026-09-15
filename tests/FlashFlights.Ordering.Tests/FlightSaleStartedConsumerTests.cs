using FlashFlights.Contracts;
using FlashFlights.Ordering.Sales;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace FlashFlights.Ordering.Tests;

/// <summary>
/// The bus seam ticket 13 adds: Catalog's announcement must reach Ordering's
/// consumer and be handed to the recorder unchanged. Notifications already
/// consumes this event; that a <em>second</em> service can is only true because
/// the shared bus wiring prefixes each queue with its service name — without
/// that the two would share one queue and each sale would reach exactly one of
/// them.
/// </summary>
public class FlightSaleStartedConsumerTests
{
    private static readonly FlightSaleStarted SaleStarted = new(
        Guid.NewGuid(),
        "FF210",
        "SFO",
        "JFK",
        new DateTimeOffset(2026, 9, 15, 14, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 9, 15, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task An_announcement_published_on_the_bus_reaches_the_recorder()
    {
        var recorder = new RecordingSaleWindowRecorder();

        await using var provider = new ServiceCollection()
            .AddSingleton<ISaleWindowRecorder>(recorder)
            .AddSingleton(NullLogger<FlightSaleStartedConsumer>.Instance)
            .AddMassTransitTestHarness(bus => bus.AddConsumer<FlightSaleStartedConsumer>())
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        await harness.Bus.Publish(SaleStarted);

        Assert.True(await harness.Consumed.Any<FlightSaleStarted>());
        Assert.Equal(SaleStarted, await recorder.First());
    }

    private sealed class RecordingSaleWindowRecorder : ISaleWindowRecorder
    {
        private readonly TaskCompletionSource<FlightSaleStarted> _first =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task OnFlightSaleStartedAsync(
            FlightSaleStarted announcement,
            CancellationToken cancellationToken = default)
        {
            _first.TrySetResult(announcement);
            return Task.CompletedTask;
        }

        public Task<FlightSaleStarted> First() => _first.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }
}
