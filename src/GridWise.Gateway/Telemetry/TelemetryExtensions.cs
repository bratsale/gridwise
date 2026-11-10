using System.Diagnostics;
using GridWise.Gateway.Authentication;
using GridWise.Gateway.RateLimiting;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.WebUtilities;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace GridWise.Gateway.Telemetry;

public static class TelemetryExtensions
{
    /// <summary>
    /// Traces, metrics and logs through OpenTelemetry, like the services (without their database and Kafka parts). Sent over OTLP only
    /// when OTEL_EXPORTER_OTLP_ENDPOINT is set. A request's trace starts here; the call to the service is a child span, and its
    /// traceparent header (set by HttpClient) makes the service's spans children of the gateway's.
    /// </summary>
    public static IHostApplicationBuilder AddGridWiseTelemetry(this IHostApplicationBuilder builder, string defaultServiceName)
    {
        var otel = builder.Services.AddOpenTelemetry();

        otel.ConfigureResource(resource =>
        {
            if (string.IsNullOrEmpty(builder.Configuration["OTEL_SERVICE_NAME"]))
                resource.AddService(defaultServiceName);
            resource.AddAttributes([new("service.instance.id", Environment.MachineName)]);
        });

        // "System.Net.Http" is the source of the outgoing calls to the services.
        otel.WithTracing(tracing => tracing
            .AddAspNetCoreInstrumentation(options =>
            {
                options.Filter = context => !context.Request.Path.StartsWithSegments("/health");
                // The events stream may carry the bearer token in ?access_token=: it must not end up in a span.
                options.EnrichWithHttpRequest = (activity, request) =>
                {
                    if (request.Query.ContainsKey(GatewayAuthentication.QueryTokenParameter))
                        activity.SetTag("url.query", RedactQueryToken(request.QueryString));
                };
            })
            .AddSource("System.Net.Http"));

        otel.WithMetrics(metrics => metrics
            .AddMeter(GatewayRateLimiting.MeterName)
            .AddAspNetCoreInstrumentation()
            .AddMeter("System.Net.Http")
            .AddRuntimeInstrumentation());

        otel.WithLogging(configureBuilder: null, configureOptions: options =>
        {
            options.IncludeScopes = true;
            options.IncludeFormattedMessage = true;
        });

        if (!string.IsNullOrEmpty(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
            otel.UseOtlpExporter();

        return builder;
    }

    /// <summary>
    /// Puts the X-Correlation-Id of the request in the log scope and on the request's span. A request without the header gets a new id,
    /// written into the request headers, so it is forwarded and the service behind uses the same one.
    /// </summary>
    public static IApplicationBuilder UseCorrelationId(this IApplicationBuilder app)
        => app.Use(async (context, next) =>
        {
            var correlationId = context.Request.Headers["X-Correlation-Id"].ToString();
            if (string.IsNullOrWhiteSpace(correlationId))
            {
                correlationId = Guid.NewGuid().ToString();
                context.Request.Headers["X-Correlation-Id"] = correlationId;
            }

            Activity.Current?.SetTag("correlation.id", correlationId);

            var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("GridWise.Correlation");
            using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
                await next();
        });

    /// <summary>The query string with the value of access_token replaced, for spans.</summary>
    public static string RedactQueryToken(QueryString query)
    {
        var redacted = new QueryBuilder();
        foreach (var (key, values) in QueryHelpers.ParseQuery(query.Value))
        {
            foreach (var value in values)
                redacted.Add(key, key == GatewayAuthentication.QueryTokenParameter ? "REDACTED" : value ?? "");
        }
        return redacted.ToQueryString().Value ?? "";
    }
}
