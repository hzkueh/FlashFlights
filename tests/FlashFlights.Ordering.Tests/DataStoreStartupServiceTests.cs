using FlashFlights.ServiceDefaults.DataStores;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FlashFlights.Ordering.Tests;

/// <summary>
/// Ordering is the service whose datastore is a separate container, so it is
/// the one that must survive starting before PostgreSQL is accepting
/// connections rather than crash-looping — and the one whose migrations are
/// most likely to run against a store that is not up yet.
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
    public async Task Keeps_retrying_until_the_migration_succeeds()
    {
        var migrator = new FlakyMigrator(failuresBeforeSuccess: 3);

        await RunToCompletionAsync(migrator, new DataStoreReadiness());

        Assert.Equal(4, migrator.Attempts);
    }

    [Fact]
    public async Task Stays_running_rather_than_crashing_while_the_datastore_is_down()
    {
        var migrator = new FlakyMigrator(failuresBeforeSuccess: int.MaxValue);

        var service = BuildService(migrator, new DataStoreReadiness());
        await service.StartAsync(CancellationToken.None);

        // Give it long enough to make several attempts, then confirm it is
        // still going rather than having faulted out of the retry loop.
        await WaitUntilAsync(() => migrator.Attempts >= 3);

        Assert.False(service.ExecuteTask!.IsCompleted);

        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Applies_the_schema_exactly_once_rather_than_on_every_readiness_hit()
    {
        var migrator = new FlakyMigrator(failuresBeforeSuccess: 0);
        var readiness = new DataStoreReadiness();

        await RunToCompletionAsync(migrator, readiness);
        await CheckHealthAsync(new SwitchableProbe { IsUp = true }, readiness);
        await CheckHealthAsync(new SwitchableProbe { IsUp = true }, readiness);

        Assert.Equal(1, migrator.Attempts);
    }

    /// <summary>
    /// The window this closes: the container is up and PostgreSQL is answering,
    /// but the tables do not exist yet. A service that calls itself ready there
    /// gets traffic it can only fail.
    /// </summary>
    [Fact]
    public async Task Reports_not_ready_while_the_datastore_is_reachable_but_unmigrated()
    {
        var health = await CheckHealthAsync(new SwitchableProbe { IsUp = true }, new DataStoreReadiness());

        Assert.Equal(HealthStatus.Unhealthy, health.Status);
        Assert.Contains("schema", health.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Reports_unhealthy_and_surfaces_the_error_while_the_datastore_is_down()
    {
        var health = await CheckHealthAsync(new SwitchableProbe { IsUp = false }, MigratedReadiness());

        Assert.Equal(HealthStatus.Unhealthy, health.Status);
        Assert.Contains("still down", health.Description);
    }

    [Fact]
    public async Task Reports_healthy_once_migrated_and_reachable()
    {
        var health = await CheckHealthAsync(new SwitchableProbe { IsUp = true }, MigratedReadiness());

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
        var readiness = MigratedReadiness();

        Assert.Equal(HealthStatus.Healthy, (await CheckHealthAsync(probe, readiness)).Status);

        probe.IsUp = false;

        Assert.Equal(HealthStatus.Unhealthy, (await CheckHealthAsync(probe, readiness)).Status);
    }

    /// <summary>
    /// Seeding a store that has no tables yet fails, so the order is not a
    /// preference — it is the only order that works.
    /// </summary>
    [Fact]
    public async Task Seeds_only_after_the_schema_is_in_place()
    {
        var migrator = new FlakyMigrator(failuresBeforeSuccess: 2);
        var seeder = new RecordingSeeder();

        await RunToCompletionAsync(migrator, new DataStoreReadiness(), seeder);

        // Three migration attempts and one seed: a seeder that ran first would
        // have been called on each of the two that failed.
        Assert.Equal(3, migrator.Attempts);
        Assert.Equal(1, seeder.Runs);
    }

    /// <summary>
    /// The window this closes is the seeding twin of the unmigrated one: a
    /// catalog that is migrated but still empty reads as a system with no
    /// flights, which is worse than one that is honestly not ready yet.
    /// </summary>
    [Fact]
    public async Task Stays_not_ready_while_the_seed_keeps_failing()
    {
        var readiness = new DataStoreReadiness();
        var seeder = new RecordingSeeder { FailuresBeforeSuccess = int.MaxValue };

        var service = BuildService(new FlakyMigrator(failuresBeforeSuccess: 0), readiness, seeder);
        await service.StartAsync(CancellationToken.None);

        await WaitUntilAsync(() => seeder.Runs >= 3);

        Assert.False(readiness.SchemaReady);

        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Retries_a_failed_seed_rather_than_giving_up_on_it()
    {
        var readiness = new DataStoreReadiness();
        var seeder = new RecordingSeeder { FailuresBeforeSuccess = 2 };

        await RunToCompletionAsync(new FlakyMigrator(failuresBeforeSuccess: 0), readiness, seeder);

        Assert.Equal(3, seeder.Runs);
        Assert.True(readiness.SchemaReady);
    }

    /// <summary>A service that registers no seeder is unaffected by any of this.</summary>
    [Fact]
    public async Task Comes_up_as_before_when_nothing_is_registered_to_seed()
    {
        var readiness = new DataStoreReadiness();

        await RunToCompletionAsync(new FlakyMigrator(failuresBeforeSuccess: 0), readiness);

        Assert.True(readiness.SchemaReady);
    }

    private static DataStoreReadiness MigratedReadiness()
    {
        var readiness = new DataStoreReadiness();
        readiness.MarkSchemaReady();

        return readiness;
    }

    private static async Task RunToCompletionAsync(
        IDataStoreMigrator migrator,
        DataStoreReadiness readiness,
        params IDataStoreSeeder[] seeders)
    {
        var service = BuildService(migrator, readiness, seeders);

        await service.StartAsync(CancellationToken.None);
        await service.ExecuteTask!.WaitAsync(TestTimeout);
        await service.StopAsync(CancellationToken.None);
    }

    private static async Task<HealthCheckResult> CheckHealthAsync(
        IDataStoreProbe probe,
        DataStoreReadiness readiness) =>
        await new DataStoreHealthCheck(probe, readiness).CheckHealthAsync(
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

    private static DataStoreStartupService BuildService(
        IDataStoreMigrator migrator,
        DataStoreReadiness readiness,
        params IDataStoreSeeder[] seeders) =>
        new(migrator, seeders, readiness, FastRetry, NullLogger<DataStoreStartupService>.Instance);

    private sealed class FlakyMigrator(int failuresBeforeSuccess) : IDataStoreMigrator
    {
        private int _attempts;

        public int Attempts => _attempts;

        public string Name => "fake-store";

        public Task MigrateAsync(CancellationToken cancellationToken)
        {
            var attempt = Interlocked.Increment(ref _attempts);

            return attempt <= failuresBeforeSuccess
                ? Task.FromException(new InvalidOperationException("still down"))
                : Task.CompletedTask;
        }
    }

    /// <summary>A seeder that counts its runs and can be told to fail the first few.</summary>
    private sealed class RecordingSeeder : IDataStoreSeeder
    {
        private int _runs;

        public int Runs => _runs;

        public int FailuresBeforeSuccess { get; init; }

        public string Name => "fake-seed";

        public Task<bool> SeedAsync(CancellationToken cancellationToken)
        {
            var run = Interlocked.Increment(ref _runs);

            return run <= FailuresBeforeSuccess
                ? Task.FromException<bool>(new InvalidOperationException("seed failed"))
                : Task.FromResult(true);
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
