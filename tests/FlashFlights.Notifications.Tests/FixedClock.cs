namespace FlashFlights.Notifications.Tests;

/// <summary>
/// A clock stopped at one instant. Everything here that reads the clock — a
/// Watch's refusal, a Notification's stamp, the demo seed — is about what is
/// true at a given moment rather than about time passing, so a fixed reading is
/// all any of them needs.
/// </summary>
internal sealed class FixedClock(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
