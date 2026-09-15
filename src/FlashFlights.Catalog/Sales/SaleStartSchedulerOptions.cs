using System.ComponentModel.DataAnnotations;

namespace FlashFlights.Catalog.Sales;

/// <summary>
/// How often the sale-start scheduler looks for Flights crossing their
/// SaleStartsAt. This interval is the lag a watcher feels: the spec promises the
/// alert "the moment the sale opens", and a crossing is only seen on the next
/// tick, so it bounds how late "the moment" can be. Kept short because the scan
/// is cheap — one indexed read that matches nothing on almost every tick.
/// </summary>
public sealed class SaleStartSchedulerOptions
{
    public const string SectionName = "SaleStartScheduler";

    /// <summary>The gap between scans.</summary>
    [Required]
    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(10);
}
