using System.Diagnostics.Metrics;
using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace GridWise.Gateway.RateLimiting;

/// <summary>
/// Fixed-window limits per user (the token's sub claim; the client IP for a request without a valid token). Each route of the
/// proxy names its policy in configuration (ReverseProxy:Routes:*:RateLimiterPolicy); the limits are in the RateLimiting section.
/// The counters live in this process's memory: with several gateway instances each would count on its own, so a user would get
/// the limit once per instance. A shared store (for example Redis) would be needed then.
/// </summary>
public static class GatewayRateLimiting
{
    public const string Recommendations = "recommendations";
    public const string Uploads = "uploads";
    public const string General = "general";

    public const string MeterName = "GridWise.Gateway";
    private static readonly Meter Meter = new(MeterName);
    private static readonly Counter<long> RateLimited = Meter.CreateCounter<long>(
        "gridwise.gateway.rate_limited", "{request}", "Requests the gateway refused with 429, by rate limit policy.");

    /// <summary>
    /// Starts each policy's series at 0. Prometheus sees no increase in a series whose first sample is already 1, so without this
    /// the first refusals after a start would not show on the dashboard. Called once the app has started (the metrics pipeline
    /// listens from then on).
    /// </summary>
    public static void InitializeCounter()
    {
        foreach (var policy in new[] { Recommendations, Uploads, General })
            RateLimited.Add(0, new KeyValuePair<string, object?>("policy", policy));
    }

    public static IHostApplicationBuilder AddGatewayRateLimiting(this IHostApplicationBuilder builder)
    {
        var section = builder.Configuration.GetSection("RateLimiting");

        builder.Services.AddRateLimiter(options =>
        {
            AddFixedWindow(options, Recommendations, section.GetSection("Recommendations"));
            AddFixedWindow(options, Uploads, section.GetSection("Uploads"));
            AddFixedWindow(options, General, section.GetSection("General"));
            options.OnRejected = WriteTooManyRequestsAsync;
        });
        return builder;
    }

    private static void AddFixedWindow(RateLimiterOptions options, string policy, IConfigurationSection limits)
    {
        var permitLimit = limits.GetValue<int?>("PermitLimit")
            ?? throw new InvalidOperationException($"{limits.Path}:PermitLimit is not configured.");
        var window = TimeSpan.FromSeconds(limits.GetValue<int?>("WindowSeconds")
            ?? throw new InvalidOperationException($"{limits.Path}:WindowSeconds is not configured."));

        options.AddPolicy(policy, http => RateLimitPartition.GetFixedWindowLimiter(PartitionKey(http), _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permitLimit,
            Window = window,
            QueueLimit = 0 // refuse at once instead of making the request wait
        }));
    }

    // Behind nginx every request comes from nginx's address, so all requests without a valid token share one partition.
    // They are refused with 401 anyway; the limit only caps how fast someone can try tokens.
    private static string PartitionKey(HttpContext http)
        => http.User.FindFirst("sub")?.Value is { Length: > 0 } sub
            ? $"user:{sub}"
            : $"ip:{http.Connection.RemoteIpAddress}";

    // 429 as ProblemDetails, with Retry-After in seconds. The fixed-window limiter reports the length of a whole window, an upper
    // bound: the current window may end sooner.
    private static async ValueTask WriteTooManyRequestsAsync(OnRejectedContext context, CancellationToken ct)
    {
        var http = context.HttpContext;
        var policy = http.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName ?? "unknown";
        RateLimited.Add(1, new KeyValuePair<string, object?>("policy", policy));

        http.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
            http.Response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);

        await http.RequestServices.GetRequiredService<IProblemDetailsService>().WriteAsync(new ProblemDetailsContext
        {
            HttpContext = http,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status429TooManyRequests,
                Title = "Too many requests",
                Detail = $"The rate limit '{policy}' was reached. Try again after the time in the Retry-After header."
            }
        });
    }
}
