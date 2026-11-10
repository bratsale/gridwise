using GridWise.Gateway.Authentication;
using GridWise.Gateway.Telemetry;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Yarp.ReverseProxy;

var builder = WebApplication.CreateBuilder(args);
builder.AddGridWiseTelemetry("gateway");
builder.AddGatewayAuthentication();

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

var app = builder.Build();

app.UseCorrelationId();
app.UseAuthentication();
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
