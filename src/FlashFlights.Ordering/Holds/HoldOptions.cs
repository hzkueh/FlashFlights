using System.ComponentModel.DataAnnotations;

namespace FlashFlights.Ordering.Holds;

/// <summary>
/// How long a Hold lives before it computes back to Available. Bound from
/// configuration so the demo can shorten it, but defaulted to the ~2 minutes
/// CONTEXT.md describes. The sweep's interval (a separate setting) must stay well
/// under this, since a silently expired Hold only reaches Catalog's SeatCounts
/// once the sweep posts its Released movement (ADR-0001).
/// </summary>
public sealed class HoldOptions
{
    public const string SectionName = "Holds";

    /// <summary>The TTL stamped onto every Hold at creation.</summary>
    [Required]
    public TimeSpan Ttl { get; set; } = TimeSpan.FromMinutes(2);
}
