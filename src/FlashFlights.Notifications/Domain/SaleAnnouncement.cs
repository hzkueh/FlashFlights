namespace FlashFlights.Notifications.Domain;

/// <summary>
/// That one Flight's sale-start announcement has reached this service. Not a
/// domain concept a buyer would name — it is how Notifications answers "has this
/// sale already opened?" without asking Catalog.
///
/// <para>
/// It exists for one rule: a Watch is a subscription to a moment still ahead
/// (CONTEXT.md), so a Flight whose sale has already opened cannot be watched —
/// there is nothing left to wait for. Notifications learns of that moment only
/// from the announcement it consumes, and takes no dependency on Catalog to ask
/// again, exactly as the seat-map relay reads a Seat's new status straight off
/// the movement event rather than reading Ordering.
/// </para>
///
/// <para>
/// One row per Flight, keyed by the Flight: a redelivered announcement finds the
/// row already there and changes nothing.
/// </para>
/// </summary>
public sealed class SaleAnnouncement
{
    /// <summary>
    /// Primary key as well as the reference to the Flight — one row per Flight.
    /// Catalog owns Flights; this is a cross-service reference, not an FK.
    /// </summary>
    public Guid FlightId { get; set; }

    /// <summary>When this service consumed the announcement — its own clock, not Catalog's.</summary>
    public DateTimeOffset AnnouncedAt { get; set; }
}
