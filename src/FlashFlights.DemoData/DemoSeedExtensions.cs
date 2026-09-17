using FlashFlights.ServiceDefaults;
using FlashFlights.ServiceDefaults.DataStores;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FlashFlights.DemoData;

/// <summary>
/// Wires one service's <see cref="IDemoSeeder"/> into the startup sequence its
/// store already runs.
/// </summary>
public static class DemoSeedExtensions
{
    /// <summary>
    /// Registers <typeparamref name="TSeeder"/> to fill this service's store on
    /// first startup. Must follow
    /// <see cref="DataStoreExtensions.AddFlashFlightsDataStore{TContext}"/>,
    /// which is what runs it.
    /// </summary>
    public static IHostApplicationBuilder AddFlashFlightsDemoSeed<TSeeder>(this IHostApplicationBuilder builder)
        where TSeeder : class, IDemoSeeder
    {
        builder.Services.AddOptions<DemoSeedOptions>()
            .Bind(builder.Configuration.GetSection(DemoSeedOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // The world is built from the seeding service's own clock. Every service
        // that seeds today registers one already, so this only keeps the call
        // self-contained rather than silently dependent on having been made
        // after whichever other call happened to register it — the same reason
        // AddFlashFlightsIdentity does it.
        builder.Services.TryAddSingleton(TimeProvider.System);

        // Scoped, because a seeder's whole job is writing through its service's
        // DbContext — which is why the adapter below opens a scope rather than
        // the startup service holding one open for the life of the process.
        builder.Services.AddScoped<TSeeder>();
        builder.Services.AddSingleton<IDataStoreSeeder, DemoDataStoreSeeder<TSeeder>>();

        return builder;
    }
}

/// <summary>
/// The one place the demo seed decides whether to run at all, what "now" is, and
/// which world every seeder is handed — so four seeders do not each have to
/// answer those three questions, and cannot answer them differently.
/// </summary>
internal sealed class DemoDataStoreSeeder<TSeeder>(
    ServiceIdentity service,
    IServiceScopeFactory scopes,
    TimeProvider clock,
    IOptions<DemoSeedOptions> options,
    ILogger<DemoDataStoreSeeder<TSeeder>> logger) : IDataStoreSeeder
    where TSeeder : class, IDemoSeeder
{
    public string Name => $"{service.Name}-demo";

    public async Task<bool> SeedAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled)
        {
            logger.LogInformation("Demo seed is disabled; {DataStore} is left as it is.", Name);

            return false;
        }

        await using var scope = scopes.CreateAsyncScope();
        var seeder = scope.ServiceProvider.GetRequiredService<TSeeder>();

        return await seeder.SeedAsync(DemoWorld.Create(clock.GetUtcNow(), options.Value), cancellationToken);
    }
}
