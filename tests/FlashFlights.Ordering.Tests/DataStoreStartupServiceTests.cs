using FlashFlights.ServiceDefaults.DataStores;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FlashFlights.Ordering.Tests;

/// <summary>
/// Ordering is the service whose datastore is a separate container, so it is
/// the one that must survive starting before PostgreSQL is accepting
/// connections rather than crash-looping.
/// </summary>
public class DataStoreStartupServiceTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    private static readonly IOptions<DataStoreRetryOptions> FastRetry =
        Options.Create(new DataStoreRetryOptions
        {
            InitialDelay = TimeSpan.FromMilliseconds(1),
            MaxDelay = TimeSpan.FromMilliseconds(5),
        });

    [Fact]
    public async Task Keeps_retrying_until_the_datastore_accepts_a_connection()
    {
        var probe = new FlakyProbe(failuresBeforeSuccess: 3);

        var service = BuildService(probe);
        await service.StartAsync(CancellationToken.None);
        await service.ExecuteTask!.WaitAsync(TestTimeout);
        await service.StopAsync(CancellationToken.None);

        Assert.Equal(4, probe.Attempts);
    }

    [Fact]
    public async Task Stays_running_rather_than_crashing_while_the_datastore_is_down()
    {
        var probe = new FlakyProbe(failuresBeforeSuccess: int.MaxValue);

        var service = BuildService(probe);
        await service.StartAsync(CancellationToken.None);

        // Give it long enough to make several attempts, then confirm it is
        // still going rather than having faulted out of the retry loop.
        await WaitUntilAsync(() => probe.Attempts >= 3);

        Assert.False(service.ExecuteTask!.IsCompleted);

        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Reports_unhealthy_and_surfaces_the_error_while_the_datastore_is_down()
    {
        var health = await CheckHealthAsync(new FlakyProbe(failuresBeforeSuccess: int.MaxValue));

        Assert.Equal(HealthStatus.Unhealthy, health.Status);
        Assert.Contains("still down", health.Description);
    }

    [Fact]
    public async Task Reports_healthy_while_the_datastore_is_reachable()
    {
        var health = await CheckHealthAsync(new FlakyProbe(failuresBeforeSuccess: 0));

        Assert.Equal(HealthStatus.Healthy, health.Status);
    }

    /// <summary>
    /// The check must probe every time rather than latch a startup result,
    /// otherwise it keeps claiming "reachable" after the database goes away.
    /// </summary>
    [Fact]
    public async Task Goes_unhealthy_again_if_the_datastore_disappears_after_being_reachable()
    {
        var probe = new SwitchableProbe { IsUp = true };

        Assert.Equal(HealthStatus.Healthy, (await CheckHealthAsync(probe)).Status);

        probe.IsUp = false;

        Assert.Equal(HealthStatus.Unhealthy, (await CheckHealthAsync(probe)).Status);
    }

    private static async Task<HealthCheckResult> CheckHealthAsync(IDataStoreProbe probe) =>
        await new DataStoreHealthCheck(probe).CheckHealthAsync(
            new HealthCheckContext
            {
                Registration = new HealthCheckRegistration(
                    "datastore", _ => null!, HealthStatus.Unhealthy, tags: null),
            },
            CancellationToken.None);

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TestTimeout;
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Condition was not met before the test timeout.");
            await Task.Delay(5, CancellationToken.None);
        }
    }

    private static DataStoreStartupService BuildService(IDataStoreProbe probe) =>
        new(probe, FastRetry, NullLogger<DataStoreStartupService>.Instance);

    private sealed class FlakyProbe(int failuresBeforeSuccess) : IDataStoreProbe
    {
        private int _attempts;

        public int Attempts => _attempts;

        public string Name => "fake-store";

        public Task ConnectAsync(CancellationToken cancellationToken)
        {
            var attempt = Interlocked.Increment(ref _attempts);

            return attempt <= failuresBeforeSuccess
                ? Task.FromException(new InvalidOperationException("still down"))
                : Task.CompletedTask;
        }
    }

    private sealed class SwitchableProbe : IDataStoreProbe
    {
        public bool IsUp { get; set; }

        public string Name => "fake-store";

        public Task ConnectAsync(CancellationToken cancellationToken) =>
            IsUp ? Task.CompletedTask : Task.FromException(new InvalidOperationException("still down"));
    }
}
