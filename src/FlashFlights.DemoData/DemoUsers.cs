namespace FlashFlights.DemoData;

/// <summary>
/// The buyers the demo world ships with, and the one password they share.
///
/// <para>
/// These credentials are public by design. A reviewer has to be able to sign in
/// as someone who already has a booking history and an inbox — registering a
/// fresh account gives them neither, and there is no admin surface that could
/// hand them one. That is also why <see cref="DemoSeedOptions.Enabled"/> exists:
/// a password in a README is right for a laptop and wrong everywhere else.
/// </para>
/// </summary>
public static class DemoUsers
{
    /// <summary>
    /// Shared by every demo buyer. Satisfies the gateway's policy — eight or
    /// more characters with an upper, a lower, and a digit — and nothing more,
    /// because a reviewer has to type it.
    /// </summary>
    public const string Password = "FlashDemo1";

    /// <summary>
    /// The account worth signing in as: bookings on two Flights, three Watches,
    /// and two alerts already in the inbox, one of them unread.
    /// </summary>
    public static readonly DemoUser Ada = new("ada", "ada@flashflights.test");

    public static readonly DemoUser Grace = new("grace", "grace@flashflights.test");

    public static readonly DemoUser Alan = new("alan", "alan@flashflights.test");

    /// <summary>Watches the Flight whose sale opens moments after the seed — the live-notification demo.</summary>
    public static readonly DemoUser Katherine = new("katherine", "katherine@flashflights.test");

    public static readonly IReadOnlyList<DemoUser> All = [Ada, Grace, Alan, Katherine];
}
