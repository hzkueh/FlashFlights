using System.ComponentModel.DataAnnotations;

namespace FlashFlights.DemoData;

/// <summary>
/// The three things about the demo seed that are about the machine it is being
/// stood up on rather than about the world itself: whether to run it, and two
/// durations that decide how long a person has to look at the result.
///
/// <para>
/// The world's <em>content</em> is not here and is not configurable — routes,
/// prices, which Seats are taken, who has booked what are all fixed in
/// <see cref="DemoWorld"/>, because a demo whose content varies by environment
/// is one nobody can describe in a bug report. The two durations below change no
/// content: the same Flights, Seats, and buyers are seeded whatever they are set
/// to.
/// </para>
/// </summary>
public sealed class DemoSeedOptions
{
    public const string SectionName = "DemoSeed";

    /// <summary>
    /// Whether to seed at all. On by default: a fresh clone coming up populated
    /// is the point of ticket 11, and this system has no flight-authoring UI to
    /// fill it any other way (CONTEXT.md).
    ///
    /// <para>
    /// The switch exists because the seed creates buyers with a password printed
    /// in this repository. That is exactly right for a local demo and exactly
    /// wrong anywhere reachable, and "turn it off" should not mean "edit the
    /// code".
    /// </para>
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// How far ahead of the seed the watch-demo Flight's sale opens. Short on
    /// purpose: the ticket asks for a Flight whose sale can be watched, and then
    /// seen to open, inside a minute.
    ///
    /// <para>
    /// Stamped once, when the store is first seeded, and never re-armed —
    /// re-arming would mean rewriting seeded rows on every restart, which is the
    /// one thing "idempotent across restarts" forbids. A stack that has been up
    /// for longer than this has already had that moment; <c>docker compose down
    /// -v</c> is what gets it back.
    /// </para>
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:05", "24:00:00")]
    public TimeSpan WatchDemoLeadTime { get; set; } = TimeSpan.FromSeconds(45);

    /// <summary>
    /// How long the seeded Held Seats stay held. Far longer than a real checkout
    /// TTL, and deliberately so: these Seats exist to show what Held looks like,
    /// and Ordering's expiry sweep would otherwise Release them partway through
    /// the first demo and leave the state unreachable without a reseed.
    /// </summary>
    [Range(typeof(TimeSpan), "00:01:00", "168:00:00")]
    public TimeSpan HeldSeatTtl { get; set; } = TimeSpan.FromHours(6);
}
