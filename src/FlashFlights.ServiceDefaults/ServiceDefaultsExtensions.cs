using System.Text.Json;
using FlashFlights.ServiceDefaults.DataStores;
using MassTransit;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;

namespace FlashFlights.ServiceDefaults;

/// <summary>
/// The host plumbing every FlashFlights service shares: bus connection,
/// datastore startup retry, and the health endpoints the gateway routes to.
/// </summary>
public static class ServiceDefaultsExtensions
{
    /// <summary>Liveness — the process is up. Never depends on a dependency being reachable.</summary>
    public const string LivenessPath = "/health";

    /// <summary>Readiness — datastore and bus are both reachable.</summary>
    public const string ReadinessPath = "/health/ready";

    private const string ReadyTag = "ready";

    /// <summary>
    /// Registers bus and datastore plumbing. The caller must already have
    /// registered its own <see cref="IDataStoreProbe"/>.
    /// </summary>
    /// <param name="builder">The host being configured.</param>
    /// <param name="serviceName">Reported by the health endpoints.</param>
    /// <param name="configureBus">Consumer registrations for this service.</param>
    public static IHostApplicationBuilder AddFlashFlightsServiceDefaults(
        this IHostApplicationBuilder builder,
        string serviceName,
        Action<IBusRegistrationConfigurator>? configureBus = null)
    {
        builder.Services.AddSingleton(new ServiceIdentity(serviceName));

        builder.Services.Configure<DataStoreRetryOptions>(builder.Configuration.GetSection("DataStoreRetry"));
        builder.Services.AddHostedService<DataStoreStartupService>();

        // MassTransit registers its own "masstransit-bus" health check, and its
        // bus reconnects on its own, so the broker needs no retry loop here.
        builder.Services.AddMassTransit(bus =>
        {
            // Queue names are prefixed with the service name so two services
            // consuming the same event each get their own queue. Without the
            // prefix they share one queue and compete for messages, which looks
            // like a working bus right up until half the events go missing.
            bus.SetEndpointNameFormatter(new KebabCaseEndpointNameFormatter(serviceName, includeNamespace: false));
            configureBus?.Invoke(bus);

            bus.UsingRabbitMq((context, cfg) =>
            {
                var broker = builder.Configuration.GetSection("RabbitMq");
                cfg.Host(
                    broker["Host"] ?? "localhost",
                    broker["VirtualHost"] ?? "/",
                    host =>
                    {
                        host.Username(broker["Username"] ?? "guest");
                        host.Password(broker["Password"] ?? "guest");
                    });

                cfg.ConfigureEndpoints(context);
            });
        });

        builder.Services
            .AddHealthChecks()
            .AddCheck<DataStoreHealthCheck>("datastore", tags: [ReadyTag]);

        return builder;
    }

    /// <summary>
    /// Maps <see cref="LivenessPath"/> and <see cref="ReadinessPath"/>. Both are
    /// anonymous so the gateway can reach them without a token.
    /// </summary>
    public static IEndpointRouteBuilder MapFlashFlightsHealth(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHealthChecks(LivenessPath, new HealthCheckOptions
        {
            // No checks: liveness must not fail just because a dependency is slow to start.
            Predicate = _ => false,
            ResponseWriter = WriteHealthResponse,
        });

        endpoints.MapHealthChecks(ReadinessPath, new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains(ReadyTag),
            ResponseWriter = WriteHealthResponse,
        });

        return endpoints;
    }

    private static Task WriteHealthResponse(HttpContext context, HealthReport report)
    {
        var identity = context.RequestServices.GetRequiredService<ServiceIdentity>();
        context.Response.ContentType = "application/json";

        return context.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            service = identity.Name,
            status = report.Status.ToString(),
            checks = report.Entries.ToDictionary(
                entry => entry.Key,
                entry => new { status = entry.Value.Status.ToString(), description = entry.Value.Description }),
        }));
    }
}

/// <summary>The service's own name, for health output and published events.</summary>
public sealed record ServiceIdentity(string Name);
