using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace FlashFlights.Ordering.Holds;

/// <summary>
/// How often the expiry sweep runs. It must stay well under <see cref="HoldOptions.Ttl"/>:
/// a silently expired Hold reaches Catalog's SeatCounts only once the sweep posts
/// its Released movement (ADR-0001), so this interval bounds how long the list
/// page's counts and a flight's own seat map may disagree. Defaulted to a
/// fraction of the ~2-minute TTL rather than a value the demo has to remember to
/// set.
/// </summary>
public sealed class HoldSweepOptions
{
    public const string SectionName = "HoldSweep";

    /// <summary>The gap between sweep runs.</summary>
    [Required]
    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(15);
}

/// <summary>
/// Refuses to start if the sweep would run no more often than Holds expire. A
/// sweep at or above the TTL is not merely slow — it lets a Hold's Released
/// movement lag its own expiry by longer than the Hold ever lived, which is the
/// exact browsing-accuracy failure the interval exists to bound. Cross-checking
/// the two options is why this cannot be a plain data annotation.
/// </summary>
public sealed class ValidateHoldSweepOptions(IOptions<HoldOptions> holdOptions) : IValidateOptions<HoldSweepOptions>
{
    private readonly TimeSpan _ttl = holdOptions.Value.Ttl;

    public ValidateOptionsResult Validate(string? name, HoldSweepOptions options)
    {
        if (options.Interval <= TimeSpan.Zero)
        {
            return ValidateOptionsResult.Fail("HoldSweep:Interval must be positive.");
        }

        if (options.Interval >= _ttl)
        {
            return ValidateOptionsResult.Fail(
                $"HoldSweep:Interval ({options.Interval}) must be well under the Hold TTL ({_ttl}); "
                + "the sweep bounds how long browsing counts may lag a silent expiry.");
        }

        return ValidateOptionsResult.Success;
    }
}
