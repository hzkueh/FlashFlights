namespace FlashFlights.Notifications.Watching;

/// <summary>
/// What became of a request to Watch a Flight. Two outcomes, not an exception
/// and not a bool: refusing a sale that has already opened is an answer the
/// caller shows the User, while watching twice is the same subscription and not
/// worth distinguishing.
/// </summary>
public enum WatchOutcome
{
    /// <summary>The User is watching this Flight — whether or not they already were.</summary>
    Watched,

    /// <summary>
    /// The sale has already opened, so there is no future moment left to be told
    /// about. A Watch fires once, when the window opens (CONTEXT.md).
    /// </summary>
    SaleAlreadyStarted,
}

/// <summary>
/// One Notification as the SPA receives it — the same shape whether it arrives
/// live over the hub or comes back from the inbox read, so the client parses one
/// Notification one way however it got there (the shape Booking already takes).
/// </summary>
/// <param name="Id">Identifies it for marking read.</param>
/// <param name="FlightId">The Flight whose sale opened, so the SPA can link to it.</param>
/// <param name="Body">What the User reads.</param>
/// <param name="CreatedAt">When the alert fired.</param>
/// <param name="ReadAt">Null while unread — the unread count is a count of nulls.</param>
public sealed record NotificationView(
    Guid Id,
    Guid FlightId,
    string Body,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ReadAt);

/// <summary>
/// A User's inbox: their Notifications newest first, and how many are unread.
/// The count travels with the list rather than being derived from it by the
/// client, so the app shell's badge means the same thing as the page.
/// </summary>
public sealed record InboxView(IReadOnlyList<NotificationView> Notifications, int UnreadCount);
