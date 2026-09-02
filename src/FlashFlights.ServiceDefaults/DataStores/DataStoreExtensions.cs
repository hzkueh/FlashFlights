using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace FlashFlights.ServiceDefaults.DataStores;

/// <summary>
/// Registers everything a service needs to own a datastore: its DbContext, the
/// startup migration with its retry, and the readiness reporting. Each service
/// calls this once with its own context and provider — Ordering with
/// PostgreSQL because its no-double-hold guarantee needs real row-level
/// locking, the others with SQLite (ADR-0001).
/// </summary>
public static class DataStoreExtensions
{
    /// <param name="builder">The host being configured.</param>
    /// <param name="dataStoreName">Reported in logs and health output, e.g. "ordering-postgres".</param>
    /// <param name="configureContext">Provider and connection string for this service's store.</param>
    public static IHostApplicationBuilder AddFlashFlightsDataStore<TContext>(
        this IHostApplicationBuilder builder,
        string dataStoreName,
        Action<DbContextOptionsBuilder> configureContext)
        where TContext : DbContext
    {
        builder.Services.AddDbContext<TContext>(configureContext);

        builder.Services.AddSingleton<IDataStoreProbe>(services =>
            new DbContextDataStoreProbe<TContext>(dataStoreName, services.GetRequiredService<IServiceScopeFactory>()));
        builder.Services.AddSingleton<IDataStoreMigrator>(services =>
            new DbContextDataStoreMigrator<TContext>(dataStoreName, services.GetRequiredService<IServiceScopeFactory>()));

        builder.Services.AddSingleton<DataStoreReadiness>();
        builder.Services.Configure<DataStoreRetryOptions>(builder.Configuration.GetSection("DataStoreRetry"));
        builder.Services.AddHostedService<DataStoreStartupService>();

        builder.Services
            .AddHealthChecks()
            .AddCheck<DataStoreHealthCheck>("datastore", tags: [ServiceDefaultsExtensions.ReadyTag]);

        return builder;
    }

    /// <summary>
    /// Reads a connection string, failing at startup rather than at the first
    /// query if it is missing.
    /// </summary>
    public static string RequireConnectionString(this IConfiguration configuration, string name) =>
        configuration.GetConnectionString(name)
        ?? throw new InvalidOperationException($"ConnectionStrings:{name} is not configured.");
}
