const string SpaCorsPolicy = "spa";

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

// Named origins rather than AllowAnyOrigin: the SPA will hold a JWT and open a
// SignalR connection, and credentialed requests forbid the wildcard.
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? ["http://localhost:5173"];

builder.Services.AddCors(options => options.AddPolicy(SpaCorsPolicy, policy => policy
    .WithOrigins(allowedOrigins)
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()));

var app = builder.Build();

app.UseCors(SpaCorsPolicy);

// The gateway's own liveness, distinct from the services it fronts.
app.MapGet("/health", () => Results.Ok(new { service = "gateway", status = "Healthy" }));

app.MapReverseProxy();

app.Run();
