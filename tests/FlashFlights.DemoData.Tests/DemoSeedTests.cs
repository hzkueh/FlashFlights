using FlashFlights.DemoData;
using FlashFlights.ServiceDefaults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FlashFlights.DemoData.Tests;

/// <summary>
/// The decisions that sit in front of every service's seeder: whether to seed at
/// all, and which world to hand it.
///
/// <para>
/// <see cref="DemoSeedOptions.Enabled"/> is a safety valve — the seed creates
/// buyers with a password printed in this repository — and an untested safety
/// valve is a decorative one.
/// </para>
/// </summary>
public class DemoSeedTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 17, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Seeds_through_the_service_s_own_seeder()
    {
        var seeder = await RunAsync(new DemoSeedOptions());

        Assert.NotNull(seeder.SeededWorld);
        Assert.Equal(DemoWorld.Create(Now, new DemoSeedOptions()).Flights.Count, seeder.SeededWorld.Flights.Count);
    }

    /// <summary>
    /// Built from the seeding service's clock, so every row it writes is relative
    /// to one instant rather than to whenever each part of it happened to run.
    /// </summary>
    [Fact]
    public async Task Builds_the_world_from_the_clock_it_was_given()
    {
        var seeder = await RunAsync(new DemoSeedOptions());

        Assert.Equal(
            DemoWorld.Create(Now, new DemoSeedOptions()).Flights.Select(flight => flight.SaleStartsAt),
            seeder.SeededWorld!.Flights.Select(flight => flight.SaleStartsAt));
    }

    [Fact]
    public async Task Turning_the_demo_seed_off_leaves_the_store_untouched()
    {
        var seeder = await RunAsync(new DemoSeedOptions { Enabled = false });

        Assert.Null(seeder.SeededWorld);
    }

    [Fact]
    public async Task Reports_whether_it_wrote_anything_so_a_restart_reads_differently_from_a_first_boot()
    {
        Assert.True(await SeedAsync(new DemoSeedOptions(), new RecordingDemoSeeder { Wrote = true }));
        Assert.False(await SeedAsync(new DemoSeedOptions(), new RecordingDemoSeeder { Wrote = false }));
        Assert.False(await SeedAsync(new DemoSeedOptions { Enabled = false }, new RecordingDemoSeeder { Wrote = true }));
    }

    /// <summary>
    /// A lead time of zero would open the watched sale at the very instant the
    /// stores are seeded, so the one moment the demo exists to show would be
    /// over before anything was listening. Caught at startup rather than noticed
    /// later as a demo that simply does not do anything.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-30)]
    public void A_watch_demo_that_could_never_be_watched_is_refused_at_startup(int leadTimeSeconds)
    {
        var options = OptionsFor(seed => seed.WatchDemoLeadTime = TimeSpan.FromSeconds(leadTimeSeconds));

        var refusal = Assert.Throws<OptionsValidationException>(() => options.Value);

        Assert.Contains(nameof(DemoSeedOptions.WatchDemoLeadTime), string.Join(" ", refusal.Failures));
    }

    [Fact]
    public void The_shipped_defaults_are_valid()
    {
        Assert.NotNull(OptionsFor(_ => { }).Value);
    }

    private static IOptions<DemoSeedOptions> OptionsFor(Action<DemoSeedOptions> configure)
    {
        var services = new ServiceCollection();

        services.AddOptions<DemoSeedOptions>()
            .Configure(configure)
            .ValidateDataAnnotations();

        return services.BuildServiceProvider().GetRequiredService<IOptions<DemoSeedOptions>>();
    }

    private static async Task<RecordingDemoSeeder> RunAsync(DemoSeedOptions options)
    {
        var seeder = new RecordingDemoSeeder();
        await SeedAsync(options, seeder);

        return seeder;
    }

    private static async Task<bool> SeedAsync(DemoSeedOptions options, RecordingDemoSeeder seeder)
    {
        var services = new ServiceCollection()
            .AddSingleton(seeder)
            .BuildServiceProvider();

        return await new DemoDataStoreSeeder<RecordingDemoSeeder>(
                new ServiceIdentity("catalog"),
                services.GetRequiredService<IServiceScopeFactory>(),
                new FixedClock(Now),
                Options.Create(options),
                NullLogger<DemoDataStoreSeeder<RecordingDemoSeeder>>.Instance)
            .SeedAsync(CancellationToken.None);
    }

    /// <summary>Stands in for a service's seeder, recording the world it was handed.</summary>
    private sealed class RecordingDemoSeeder : IDemoSeeder
    {
        public bool Wrote { get; init; } = true;

        public DemoWorld? SeededWorld { get; private set; }

        public Task<bool> SeedAsync(DemoWorld world, CancellationToken cancellationToken = default)
        {
            SeededWorld = world;

            return Task.FromResult(Wrote);
        }
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
