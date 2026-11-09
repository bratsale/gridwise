using System.Diagnostics;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace GridWise.Batch.Telemetry;

public static class TelemetryExtensions
{
    /// <summary>
    /// Traces, metrics and logs through OpenTelemetry. Everything is sent over OTLP only when OTEL_EXPORTER_OTLP_ENDPOINT is set
    /// (compose sets it), so tests and a plain "dotnet run" export nothing. The console logs stay as they are.
    /// The service name comes from OTEL_SERVICE_NAME; <paramref name="defaultServiceName"/> is used when it is not set.
    /// </summary>
    public static IHostApplicationBuilder AddGridWiseTelemetry(this IHostApplicationBuilder builder, string defaultServiceName)
    {
        var otel = builder.Services.AddOpenTelemetry();

        otel.ConfigureResource(resource =>
        {
            if (string.IsNullOrEmpty(builder.Configuration["OTEL_SERVICE_NAME"]))
                resource.AddService(defaultServiceName);
        });

        // "Npgsql" is the source of the database spans (EF Core runs its SQL through Npgsql).
        otel.WithTracing(tracing => tracing
            .SetSampler(new ParentBasedSampler(new DropDatabaseSpansWithoutParent()))
            .AddSource(Tracing.SourceName)
            .AddSource("Npgsql")
            .AddAspNetCoreInstrumentation(options =>
                options.Filter = context => !context.Request.Path.StartsWithSegments("/health")));

        otel.WithMetrics(metrics => metrics
            .AddAspNetCoreInstrumentation()
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
    /// written into the request headers, so the endpoint that stores it in an event uses the same one.
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
}

/// <summary>
/// The outbox publisher, the dispatcher and the retention job query the database every second or so. Those queries have no parent, and
/// each would be a trace of its own (thousands an hour, all empty). Dropping a database (client) span without a parent removes them; a query that is
/// part of a request or of a message has a parent and is kept, and so are all other root spans (a request, a dispatched chunk).
/// </summary>
internal sealed class DropDatabaseSpansWithoutParent : Sampler
{
    public override SamplingResult ShouldSample(in SamplingParameters parameters)
        => parameters.Kind == ActivityKind.Client // the only client spans are Npgsql's; their name is not final when the sampler runs
            ? new SamplingResult(SamplingDecision.Drop)
            : new SamplingResult(SamplingDecision.RecordAndSample);
}
