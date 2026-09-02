using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace FlashFlights.ServiceDefaults.DataStores;

/// <summary>
/// Readiness check for the service's datastore: the schema must have been
/// migrated, and the store must still be answering right now.
///
/// The connectivity half deliberately does not cache a startup result — a
/// latched flag would keep reporting "reachable" after the database went away,
/// which is exactly the case a readiness endpoint exists to catch. The probe
/// rides the connection pool, so a hit on a healthy service is cheap. The
/// schema half is latched, because migrations genuinely only happen once.
/// </summary>
public sealed class DataStoreHealthCheck(IDataStoreProbe probe, DataStoreReadiness readiness) : IHealthCheck
{
    /// <summary>Bounds the check so a dead datastore fails fast instead of hanging on the driver's own connect timeout.</summary>
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(2);

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (!readiness.SchemaReady)
        {
            return new HealthCheckResult(
                context.Registration.FailureStatus,
                $"{probe.Name} schema has not been migrated yet");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ProbeTimeout);

        try
        {
            await probe.ConnectAsync(timeout.Token);
            return HealthCheckResult.Healthy($"{probe.Name} reachable");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new HealthCheckResult(
                context.Registration.FailureStatus,
                $"{probe.Name} did not answer within {ProbeTimeout.TotalSeconds:0}s");
        }
        catch (Exception ex)
        {
            return new HealthCheckResult(
                context.Registration.FailureStatus,
                $"{probe.Name} not reachable: {ex.Message}",
                ex);
        }
    }
}
