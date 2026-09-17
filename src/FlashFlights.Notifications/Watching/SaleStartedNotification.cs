namespace FlashFlights.Notifications.Watching;

/// <summary>
/// What a fired Watch tells its User. One sentence, in one place, because the
/// demo seed writes Notifications that must be indistinguishable from the ones
/// the dispatcher writes — a second copy of this format would drift, and the
/// difference would only ever show up as two Notifications in one inbox that
/// read differently for no reason.
/// </summary>
internal static class SaleStartedNotification
{
    /// <summary>
    /// Composed from the Flight's route alone. When the dispatcher writes one,
    /// everything in this sentence had to travel on the announcement —
    /// Notifications takes no dependency on Catalog.
    /// </summary>
    public static string Body(string flightNumber, string origin, string destination) =>
        $"{flightNumber} {origin} to {destination} is now on sale";
}
