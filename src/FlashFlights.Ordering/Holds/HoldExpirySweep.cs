using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FlashFlights.Ordering.Holds;

/// <summary>
/// Runs <see cref="IHoldService.ExpireHoldsAsync"/> on a fixed interval. The read
/// side already heals itself — a Hold past its TTL computes back to Available
/// without this having run (ADR-0001) — so this service is not what makes expiry
/// correct within Ordering. It is what makes expiry <em>visible</em>: only the
/// Released movement it posts reaches Catalog's SeatCounts, so this loop is
/// load-bearing for how fresh the list page's counts stay.
/// </summary>
public sealed class HoldExpirySweep(
    IServiceScopeFactory scopeFactory,
    TimeProvider clock,
    IOptions<HoldSweepOptions> options,
    ILogger<HoldExpirySweep> logger) : BackgroundService
{
    private readonly TimeSpan _interval = options.Value.Interval;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // The same clock the read side and the writes share, so a test that drives
        // time also drives the timer if it ever needs to.
        using var timer = new PeriodicTimer(_interval, clock);

        while (await SafeWaitAsync(timer, stoppingToken))
        {
            try
            {
                await SweepOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                // A failed run must not kill the loop — the next tick tries again,
                // and the read side keeps expiry correct in the meantime.
                logger.LogError(exception, "Hold expiry sweep run failed; retrying at the next interval.");
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

    private async Task SweepOnceAsync(CancellationToken cancellationToken)
    {
        // A fresh scope per run: the background service is a singleton but the
        // DbContext and HoldService are scoped.
        using var scope = scopeFactory.CreateScope();
        var holds = scope.ServiceProvider.GetRequiredService<IHoldService>();

        var result = await holds.ExpireHoldsAsync(cancellationToken);

        if (result.SeatsReleased > 0)
        {
            logger.LogInformation(
                "Hold expiry sweep released {SeatsReleased} seat(s) across {HoldsExpired} hold(s).",
                result.SeatsReleased,
                result.HoldsExpired);
        }
    }
}
