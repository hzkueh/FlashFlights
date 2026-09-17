using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FlashFlights.ServiceDefaults.DataStores;

/// <summary>
/// Brings the service's store up on startup — migrations first, then whatever
/// seeding it registered — retrying with capped exponential backoff instead of
/// letting the process crash-loop when the container starts before its database
/// does. Never throws: the service stays up, and
/// <see cref="DataStoreHealthCheck"/> reports it not-ready until the store is in
/// place.
///
/// <para>
/// Seeding runs inside the same attempt as the migration and before readiness is
/// marked, so a store is never served while it is migrated but still empty, and a
/// seeder that fails is retried with the same backoff as a database that is not
/// up yet. Both halves must therefore tolerate being run again — see
/// <see cref="IDataStoreSeeder"/>.
/// </para>
/// </summary>
public sealed class DataStoreStartupService(
    IDataStoreMigrator migrator,
    IEnumerable<IDataStoreSeeder> seeders,
    DataStoreReadiness readiness,
    IOptions<DataStoreRetryOptions> options,
    ILogger<DataStoreStartupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var retry = options.Value;
        var delay = retry.InitialDelay;

        for (var attempt = 1; !stoppingToken.IsCancellationRequested; attempt++)
        {
            try
            {
                await migrator.MigrateAsync(stoppingToken);
                await SeedAsync(stoppingToken);
                readiness.MarkSchemaReady();
                logger.LogInformation(
                    "Datastore {DataStore} migrated after {Attempts} attempt(s)", migrator.Name, attempt);
                return;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "Datastore {DataStore} could not be migrated (attempt {Attempt}); retrying in {Delay}",
                    migrator.Name,
                    attempt,
                    delay);
            }

            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            delay = delay >= retry.MaxDelay ? retry.MaxDelay : TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, retry.MaxDelay.Ticks));
        }
    }

    private async Task SeedAsync(CancellationToken stoppingToken)
    {
        foreach (var seeder in seeders)
        {
            // Left to throw: a failed seed must not be mistaken for an empty
            // store, so the attempt is abandoned and retried rather than the
            // service reporting ready with half a world in it.
            var seeded = await seeder.SeedAsync(stoppingToken);

            if (seeded)
            {
                logger.LogInformation("Datastore {DataStore} seeded by {Seeder}.", migrator.Name, seeder.Name);
            }
        }
    }
}

/// <summary>Backoff schedule for <see cref="DataStoreStartupService"/>.</summary>
public sealed class DataStoreRetryOptions
{
    public TimeSpan InitialDelay { get; set; } = TimeSpan.FromSeconds(1);

    public TimeSpan MaxDelay { get; set; } = TimeSpan.FromSeconds(15);
}
