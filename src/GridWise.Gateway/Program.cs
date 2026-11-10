using System.Net;
using GridWise.Gateway.Authentication;
using Microsoft.AspNetCore.HttpOverrides;
using GridWise.Gateway.RateLimiting;
using GridWise.Gateway.Telemetry;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Yarp.ReverseProxy;

var builder = WebApplication.CreateBuilder(args);
builder.AddGridWiseTelemetry("gateway");
builder.AddGatewayAuthentication();
builder.AddGatewayRateLimiting();

// Routes and clusters come from the "ReverseProxy" section: appsettings.json, overridden by environment variables
// (for example ReverseProxy__Clusters__catalog__Destinations__main__Address).
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddTransforms(context =>
    {
        if (context.Route.RouteId == "batch-events")
            GatewayAuthentication.AddQueryTokenForwarding(context);
    });

// Ready once the proxy has routes to serve. The services behind it have their own health checks.
builder.Services.AddHealthChecks().AddCheck<RoutesLoadedCheck>("routes-loaded", tags: ["ready"]);

// The client's address comes from X-Forwarded-For, but only when the request comes from nginx (its fixed address on the edge network,
// ForwardedHeaders:KnownProxies). From anyone else the header is ignored, so it cannot be used to pick someone else's rate limit.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor;
    options.ForwardLimit = 1;
    options.KnownProxies.Clear();
    options.KnownIPNetworks.Clear();
    foreach (var proxy in builder.Configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [])
        options.KnownProxies.Add(IPAddress.Parse(proxy));
});

var app = builder.Build();
app.Lifetime.ApplicationStarted.Register(GatewayRateLimiting.InitializeCounter);

app.UseForwardedHeaders();
app.UseCorrelationId();
app.UseAuthentication();
// After authentication, so the user (sub) is known; before authorization, so requests with bad tokens are limited too (per IP).
app.UseRateLimiter();
app.UseAuthorization();

app.MapHealthChecks("/health/live", new() { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/health/ready", new() { Predicate = c => c.Tags.Contains("ready") }).AllowAnonymous();
app.MapReverseProxy();

app.Run();

internal sealed class RoutesLoadedCheck(IProxyStateLookup proxy) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        => Task.FromResult(proxy.GetRoutes().Any()
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("No routes are configured (section ReverseProxy)."));
}

public partial class Program;
