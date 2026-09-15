using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace FlashFlights.Notifications.Watching;

/// <summary>
/// Teaches the bearer scheme to read a token off the WebSocket handshake. A
/// browser cannot set an Authorization header on a WebSocket, so the SignalR
/// client sends its token as the <c>access_token</c> query parameter instead;
/// without this the notifications hub would refuse every connection despite a
/// perfectly good token.
///
/// <para>
/// Deliberately scoped to the hub path and nothing else. A token in a query
/// string is a token in server logs and browser history, so it is accepted only
/// where the transport leaves no alternative — every ordinary request to this
/// service still has to carry a header.
/// </para>
///
/// <para>
/// This configures the scheme that
/// <c>AddFlashFlightsServiceDefaults</c> already registered, so it must be called
/// after it: it adds the events and leaves the shared validation rules — issuer,
/// audience, signing key — exactly as they were.
/// </para>
/// </summary>
public static class NotificationsHubAuthentication
{
    /// <summary>The path the hub is mapped at, relative to this service.</summary>
    public const string HubPath = "/hubs/notifications";

    public static IHostApplicationBuilder AddNotificationsHubAuthentication(this IHostApplicationBuilder builder)
    {
        builder.Services.Configure<JwtBearerOptions>(
            JwtBearerDefaults.AuthenticationScheme,
            options => options.Events = new JwtBearerEvents
            {
                OnMessageReceived = context =>
                {
                    if (context.Request.Path.StartsWithSegments(HubPath))
                    {
                        var token = context.Request.Query["access_token"];

                        if (!string.IsNullOrEmpty(token))
                        {
                            context.Token = token;
                        }
                    }

                    return Task.CompletedTask;
                },
            });

        return builder;
    }
}
