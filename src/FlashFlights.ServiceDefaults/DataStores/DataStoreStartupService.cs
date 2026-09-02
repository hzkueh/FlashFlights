using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FlashFlights.ServiceDefaults.DataStores;

/// <summary>
/// Applies the service's migrations on startup, retrying with capped
/// exponential backoff instead of letting the process crash-loop when the
/// container starts before its database does. Never throws: the service stays
/// up, and <see cref="DataStoreHealthCheck"/> reports it not-ready until the
/// schema is in place.
/// </summary>
public sealed class DataStoreStartupService(
    IDataStoreMigrator migrator,
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
}

/// <summary>Backoff schedule for <see cref="DataStoreStartupService"/>.</summary>
public sealed class DataStoreRetryOptions
{
    public TimeSpan InitialDelay { get; set; } = TimeSpan.FromSeconds(1);

    public TimeSpan MaxDelay { get; set; } = TimeSpan.FromSeconds(15);
}
