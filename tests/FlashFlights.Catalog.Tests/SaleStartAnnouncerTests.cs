using FlashFlights.Catalog.Sales;
using FlashFlights.Contracts;
using Microsoft.Extensions.Logging.Abstractions;

namespace FlashFlights.Catalog.Tests;

/// <summary>
/// Catalog's half of ticket 09: the scheduler that detects a Flight crossing its
/// SaleStartsAt and announces it exactly once. Exercised directly against a fake
/// notifier — the background loop that calls it is a timer, and what needs
/// proving is which Flights get announced, how often, and what the announcement
/// says.
/// </summary>
public class SaleStartAnnouncerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_flight_whose_window_has_opened_is_announced()
    {
        using var testDb = CatalogTestDb.Create();
        var flightId = await testDb.SeedFlightAsync(
            saleStartsAt: Now.AddMinutes(-1),
            saleEndsAt: Now.AddHours(2));

        var notifier = new RecordingFlightSaleNotifier();
        var result = await AnnouncerFor(testDb, notifier).AnnounceStartedSalesAsync();

        Assert.Equal(1, result.Announced);
        Assert.Equal(flightId, Assert.Single(notifier.Announcements).FlightId);
    }

    /// <summary>
    /// The exactly-once rule the ticket names. A second run finds the Flight
    /// already handled and says nothing, so a watcher is told once no matter how
    /// often the scheduler ticks.
    /// </summary>
    [Fact]
    public async Task A_flight_is_never_announced_twice()
    {
        using var testDb = CatalogTestDb.Create();
        await testDb.SeedFlightAsync(saleStartsAt: Now.AddMinutes(-1), saleEndsAt: Now.AddHours(2));

        var notifier = new RecordingFlightSaleNotifier();
        await AnnouncerFor(testDb, notifier).AnnounceStartedSalesAsync();
        var second = await AnnouncerFor(testDb, notifier).AnnounceStartedSalesAsync();

        Assert.Equal(0, second.Announced);
        Assert.Single(notifier.Announcements);
    }

    [Fact]
    public async Task A_sale_that_has_not_opened_yet_is_left_alone()
    {
        using var testDb = CatalogTestDb.Create();
        await testDb.SeedFlightAsync(saleStartsAt: Now.AddMinutes(1), saleEndsAt: Now.AddHours(2));

        var notifier = new RecordingFlightSaleNotifier();
        var result = await AnnouncerFor(testDb, notifier).AnnounceStartedSalesAsync();

        Assert.Equal(0, result.Announced);
        Assert.Empty(notifier.Announcements);
    }

    /// <summary>
    /// A window that opened and closed unseen — seed data for an already-ended
    /// sale, or a service that was down across the whole window — is still
    /// announced. Catalog does not decide who hears about it: the announcement
    /// carries SaleEndsAt, and Notifications is the one that turns a closed
    /// window into "tell no one" while still recording that this sale's moment
    /// has passed. Deciding it here would leave Notifications unable to tell an
    /// ended sale from one that has not opened, and so willing to accept a Watch
    /// that could never fire.
    /// </summary>
    [Fact]
    public async Task A_window_that_already_closed_is_still_announced()
    {
        using var testDb = CatalogTestDb.Create();
        var saleEndsAt = Now.AddHours(-1);
        await testDb.SeedFlightAsync(saleStartsAt: Now.AddHours(-3), saleEndsAt: saleEndsAt);

        var notifier = new RecordingFlightSaleNotifier();
        var result = await AnnouncerFor(testDb, notifier).AnnounceStartedSalesAsync();

        Assert.Equal(1, result.Announced);
        Assert.Equal(saleEndsAt, Assert.Single(notifier.Announcements).SaleEndsAt);

        // And still exactly once: the marker settles it for good.
        var second = await AnnouncerFor(testDb, notifier).AnnounceStartedSalesAsync();
        Assert.Equal(0, second.Announced);
        Assert.Single(notifier.Announcements);
    }

    /// <summary>
    /// Notifications takes no dependency on Catalog, so everything a watcher is
    /// told has to travel on the announcement itself.
    /// </summary>
    [Fact]
    public async Task The_announcement_carries_the_route_and_the_deadline()
    {
        using var testDb = CatalogTestDb.Create();
        var saleEndsAt = Now.AddHours(2);
        await testDb.SeedFlightAsync(
            flightNumber: "FF901",
            saleStartsAt: Now.AddMinutes(-1),
            saleEndsAt: saleEndsAt);

        var notifier = new RecordingFlightSaleNotifier();
        await AnnouncerFor(testDb, notifier).AnnounceStartedSalesAsync();

        var announcement = Assert.Single(notifier.Announcements);
        Assert.Equal("FF901", announcement.FlightNumber);
        Assert.Equal("LHR", announcement.Origin);
        Assert.Equal("BCN", announcement.Destination);
        Assert.Equal(saleEndsAt, announcement.SaleEndsAt);
        Assert.Equal(Now, announcement.OccurredAt);
    }

    /// <summary>
    /// The one place this differs from Ordering's best-effort seat-movement
    /// publish: there, a lost event only leaves advisory counts stale. Here the
    /// announcement <em>is</em> the feature, so a Flight is marked only once it
    /// has actually been announced, and a failed publish is retried on the next
    /// tick rather than swallowed into a silence no one can recover.
    /// </summary>
    [Fact]
    public async Task A_failed_announcement_is_retried_on_the_next_run()
    {
        using var testDb = CatalogTestDb.Create();
        await testDb.SeedFlightAsync(saleStartsAt: Now.AddMinutes(-1), saleEndsAt: Now.AddHours(2));

        var notifier = new RecordingFlightSaleNotifier { FailNext = true };
        var first = await AnnouncerFor(testDb, notifier).AnnounceStartedSalesAsync();

        Assert.Equal(0, first.Announced);
        Assert.Empty(notifier.Announcements);

        var second = await AnnouncerFor(testDb, notifier).AnnounceStartedSalesAsync();

        Assert.Equal(1, second.Announced);
        Assert.Single(notifier.Announcements);
    }

    /// <summary>One Flight's failure must not stop the others in the same run.</summary>
    [Fact]
    public async Task One_failed_flight_does_not_block_the_rest_of_the_run()
    {
        using var testDb = CatalogTestDb.Create();
        await testDb.SeedFlightAsync(flightNumber: "FF001", saleStartsAt: Now.AddMinutes(-2), saleEndsAt: Now.AddHours(2));
        await testDb.SeedFlightAsync(flightNumber: "FF002", saleStartsAt: Now.AddMinutes(-1), saleEndsAt: Now.AddHours(2));

        var notifier = new RecordingFlightSaleNotifier { FailNext = true };
        var result = await AnnouncerFor(testDb, notifier).AnnounceStartedSalesAsync();

        Assert.Equal(1, result.Announced);
        Assert.Equal("FF002", Assert.Single(notifier.Announcements).FlightNumber);
    }

    private static SaleStartAnnouncer AnnouncerFor(CatalogTestDb testDb, IFlightSaleNotifier notifier) =>
        new(testDb.NewContext(), notifier, new FixedClock(Now), NullLogger<SaleStartAnnouncer>.Instance);

    private sealed class RecordingFlightSaleNotifier : IFlightSaleNotifier
    {
        private readonly List<FlightSaleStarted> _announcements = [];

        /// <summary>Fails the very next publish, then behaves — a broker blip, not an outage.</summary>
        public bool FailNext { get; set; }

        public IReadOnlyList<FlightSaleStarted> Announcements => _announcements;

        public Task SaleStartedAsync(FlightSaleStarted announcement, CancellationToken cancellationToken = default)
        {
            if (FailNext)
            {
                FailNext = false;
                return Task.FromException(new InvalidOperationException("Broker unreachable."));
            }

            _announcements.Add(announcement);
            return Task.CompletedTask;
        }
    }
}
