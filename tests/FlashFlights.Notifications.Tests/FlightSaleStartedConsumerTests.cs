using FlashFlights.Contracts;
using FlashFlights.Notifications.Watching;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace FlashFlights.Notifications.Tests;

/// <summary>
/// The bus seam between the two halves of ticket 09: an announcement Catalog
/// publishes must reach this service's consumer and be handed to the dispatcher
/// unchanged. Exercised through the harness against a recording dispatcher — who
/// gets told what is proven separately; what needs proving here is that the
/// message arrives at all.
/// </summary>
public class FlightSaleStartedConsumerTests
{
    private static readonly FlightSaleStarted SaleStarted = new(
        Guid.NewGuid(),
        "FF412",
        "LHR",
        "BCN",
        new DateTimeOffset(2026, 9, 15, 14, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 9, 15, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task An_announcement_published_on_the_bus_reaches_the_dispatcher()
    {
        var dispatcher = new RecordingDispatcher();

        await using var provider = new ServiceCollection()
            .AddSingleton<IWatchNotificationDispatcher>(dispatcher)
            .AddSingleton(NullLogger<FlightSaleStartedConsumer>.Instance)
            .AddMassTransitTestHarness(bus => bus.AddConsumer<FlightSaleStartedConsumer>())
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        await harness.Bus.Publish(SaleStarted);

        Assert.True(await harness.Consumed.Any<FlightSaleStarted>());
        Assert.Equal(SaleStarted, await dispatcher.First());
    }

    private sealed class RecordingDispatcher : IWatchNotificationDispatcher
    {
        private readonly TaskCompletionSource<FlightSaleStarted> _first =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<int> OnFlightSaleStartedAsync(
            FlightSaleStarted announcement,
            CancellationToken cancellationToken = default)
        {
            _first.TrySetResult(announcement);
            return Task.FromResult(1);
        }

        public Task<FlightSaleStarted> First() => _first.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }
}
