using FlashFlights.Contracts;
using FlashFlights.ServiceDefaults.Wiring;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace FlashFlights.Notifications.Tests;

/// <summary>
/// The bus seam for ticket 01: a <see cref="PingSent"/> published anywhere on
/// the bus must reach this service's consumer and be observable afterwards.
/// </summary>
public class PingSentConsumerTests
{
    [Fact]
    public async Task Records_a_published_ping_so_it_is_observable_afterwards()
    {
        await using var provider = BuildProvider();
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var ping = new PingSent(Guid.NewGuid(), "catalog", DateTimeOffset.UtcNow);
        await harness.Bus.Publish(ping);

        Assert.True(await harness.Consumed.Any<PingSent>());

        var recorded = Assert.Single(provider.GetRequiredService<IPingLog>().Received);
        Assert.Equal(ping.PingId, recorded.PingId);
        Assert.Equal("catalog", recorded.Source);
    }

    [Fact]
    public async Task Records_every_ping_it_consumes()
    {
        await using var provider = BuildProvider();
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var first = new PingSent(Guid.NewGuid(), "catalog", DateTimeOffset.UtcNow);
        var second = new PingSent(Guid.NewGuid(), "catalog", DateTimeOffset.UtcNow);
        await harness.Bus.Publish(first);
        await harness.Bus.Publish(second);

        Assert.True(await harness.Consumed.Any<PingSent>(x => x.Context.Message.PingId == first.PingId));
        Assert.True(await harness.Consumed.Any<PingSent>(x => x.Context.Message.PingId == second.PingId));

        var recorded = provider.GetRequiredService<IPingLog>().Received.Select(p => p.PingId).ToHashSet();
        Assert.Equal([first.PingId, second.PingId], recorded);
    }

    private static ServiceProvider BuildProvider() =>
        new ServiceCollection()
            .AddSingleton<IPingLog, InMemoryPingLog>()
            .AddMassTransitTestHarness(bus => bus.AddConsumer<PingSentConsumer>())
            .BuildServiceProvider(true);
}
