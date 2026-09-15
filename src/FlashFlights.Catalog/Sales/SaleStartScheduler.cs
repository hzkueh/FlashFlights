using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FlashFlights.Catalog.Sales;

/// <summary>
/// Runs <see cref="SaleStartAnnouncer.AnnounceStartedSalesAsync"/> on a fixed
/// interval — the scheduler the ticket asks Catalog to run, and nothing more.
/// All the judgement about which Flights to announce lives in the announcer; this
/// is the timer that gives it a heartbeat.
///
/// <para>
/// Unlike Ordering's expiry sweep, this loop is load-bearing rather than merely
/// freshening: no read path anywhere recomputes "this sale just opened", so a
/// crossing that is never scanned is an alert that never fires. That is why a
/// failed run is logged and retried rather than allowed to end the loop, and why
/// the announcer leaves a Flight it could not announce unmarked.
/// </para>
/// </summary>
public sealed class SaleStartScheduler(
    IServiceScopeFactory scopeFactory,
    TimeProvider clock,
    IOptions<SaleStartSchedulerOptions> options,
    ILogger<SaleStartScheduler> logger) : BackgroundService
{
    private readonly TimeSpan _interval = options.Value.Interval;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // The same clock the announcer reads, so a test that drives time drives
        // the timer with it.
        using var timer = new PeriodicTimer(_interval, clock);

        while (await SafeWaitAsync(timer, stoppingToken))
        {
            try
            {
                await ScanOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                // A failed run must not kill the loop: the Flights it did not
                // settle are still unmarked, so the next tick finds them again.
                logger.LogError(exception, "Sale-start scan failed; retrying at the next interval.");
            }
        }
    }

    private static async Task<bool> SafeWaitAsync(PeriodicTimer timer, CancellationToken stoppingToken)
    {
        try
        {
            return await timer.WaitForNextTickAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private async Task ScanOnceAsync(CancellationToken cancellationToken)
    {
        // A fresh scope per run: this service is a singleton, the DbContext and
        // the announcer are scoped.
        using var scope = scopeFactory.CreateScope();
        var announcer = scope.ServiceProvider.GetRequiredService<SaleStartAnnouncer>();

        var result = await announcer.AnnounceStartedSalesAsync(cancellationToken);

        if (result.Announced > 0)
        {
            logger.LogInformation("Sale-start scan announced {Announced} flight(s).", result.Announced);
        }
    }
}
