namespace FlashFlights.Notifications.Domain;

/// <summary>
/// A delivered alert to a User, persisted so it is still there on their next
/// visit whether or not they were connected when it fired (CONTEXT.md).
/// </summary>
public sealed class Notification
{
    public Guid Id { get; set; }

    /// <summary>The Identity store owns Users; this is a cross-service reference, not an FK.</summary>
    public Guid UserId { get; set; }

    /// <summary>The Flight whose sale went live.</summary>
    public Guid FlightId { get; set; }

    /// <summary>What the User is told, e.g. "FF412 LHR to BCN is now on sale".</summary>
    public required string Body { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Null until the User reads it — the unread count is a count of nulls.</summary>
    public DateTimeOffset? ReadAt { get; set; }
}
