using FlashFlights.Ordering.Holds;
using Microsoft.Extensions.Options;

namespace FlashFlights.Ordering.Tests;

/// <summary>
/// The sweep interval is only useful if it is well under the Hold TTL — a silent
/// expiry reaches Catalog only via the Released movement the sweep posts, so a
/// sweep no more frequent than expiry lets browsing counts lag by longer than a
/// Hold ever lives. These prove the cross-option guard refuses that at startup
/// rather than letting it ship as a quiet misconfiguration.
/// </summary>
public class HoldSweepOptionsTests
{
    private static ValidateHoldSweepOptions ValidatorWithTtl(TimeSpan ttl) =>
        new(Options.Create(new HoldOptions { Ttl = ttl }));

    [Fact]
    public void Accepts_an_interval_well_under_the_ttl()
    {
        var result = ValidatorWithTtl(TimeSpan.FromMinutes(2))
            .Validate(null, new HoldSweepOptions { Interval = TimeSpan.FromSeconds(15) });

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Rejects_an_interval_at_or_beyond_the_ttl()
    {
        var result = ValidatorWithTtl(TimeSpan.FromMinutes(2))
            .Validate(null, new HoldSweepOptions { Interval = TimeSpan.FromMinutes(2) });

        Assert.True(result.Failed);
    }

    [Fact]
    public void Rejects_a_non_positive_interval()
    {
        var result = ValidatorWithTtl(TimeSpan.FromMinutes(2))
            .Validate(null, new HoldSweepOptions { Interval = TimeSpan.Zero });

        Assert.True(result.Failed);
    }
}
