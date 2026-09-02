namespace FlashFlights.Notifications.Domain;

/// <summary>
/// A User's subscription to be told when a specific Flight's sale goes live —
/// the only trigger in the MVP (CONTEXT.md).
///
/// Un-watching deletes the row: there is no watch history worth keeping, and a
/// deleted Watch cannot be matched by a later FlightSaleStarted, which is
/// exactly what "stop notifying me" has to mean.
/// </summary>
public sealed class Watch
{
    public Guid Id { get; set; }

    /// <summary>The Identity store owns Users; this is a cross-service reference, not an FK.</summary>
    public Guid UserId { get; set; }

    /// <summary>Catalog owns Flights; this is a cross-service reference, not an FK.</summary>
    public Guid FlightId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
