using Confluent.Kafka;
using GridWise.Batch.Api;
using GridWise.Batch.Data;
using GridWise.Batch.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Scalar.AspNetCore;

if (args.Contains("reprocess-dlq"))
    return await ReprocessDlqAsync(args);

var builder = WebApplication.CreateBuilder(args);

if (string.IsNullOrEmpty(builder.Configuration["Kafka:BootstrapServers"]))
    throw new InvalidOperationException("Kafka:BootstrapServers is not configured.");

builder.Services.AddDbContext<BatchDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Batch"),
            npgsql => npgsql.EnableRetryOnFailure())
        .UseSnakeCaseNamingConvention());

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks().AddDbContextCheck<BatchDbContext>(tags: ["ready"]);
builder.Services.AddScoped<BatchResultHandler>();
builder.Services.AddHostedService<BatchResultsConsumer>();
builder.Services.AddHostedService<BatchDispatcher>();
builder.Services.AddHostedService<OutboxPublisher>();
builder.Services.AddHostedService<RetentionCleanup>();

var app = builder.Build();

const int maxAttempts = 30;
for (var attempt = 1; ; attempt++)
{
    try
    {
        using var scope = app.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<BatchDbContext>().Database.Migrate();
        break;
    }
    catch (Exception ex) when (ex is NpgsqlException or RetryLimitExceededException && attempt < maxAttempts)
    {
        app.Logger.LogWarning("Migration attempt {Attempt} of {MaxAttempts} failed: {Message}", attempt, maxAttempts, ex.Message);
        await Task.Delay(TimeSpan.FromSeconds(2));
    }
}

app.MapOpenApi();
app.MapHealthChecks("/health/live", new() { Predicate = _ => false }).ExcludeFromDescription();
app.MapHealthChecks("/health/ready", new() { Predicate = c => c.Tags.Contains("ready") }).ExcludeFromDescription();
if (app.Environment.IsDevelopment())
    app.MapScalarApiReference();

app.MapJobEndpoints();

app.Run();
return 0;

// One-off command: no web app, no hosted services.
static async Task<int> ReprocessDlqAsync(string[] args)
{
    var builder = Host.CreateApplicationBuilder(args);
    var bootstrapServers = builder.Configuration["Kafka:BootstrapServers"];
    if (string.IsNullOrEmpty(bootstrapServers))
        throw new InvalidOperationException("Kafka:BootstrapServers is not configured.");

    builder.Services.AddDbContext<BatchDbContext>(options =>
        options.UseNpgsql(builder.Configuration.GetConnectionString("Batch"),
                npgsql => npgsql.EnableRetryOnFailure())
            .UseSnakeCaseNamingConvention());
    builder.Services.AddSingleton(TimeProvider.System);

    using var host = builder.Build();
    var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("DlqReprocessor");

    const int maxAttempts = 30;
    for (var attempt = 1; ; attempt++)
    {
        try
        {
            using var scope = host.Services.CreateScope();
            scope.ServiceProvider.GetRequiredService<BatchDbContext>().Database.Migrate();
            break;
        }
        catch (Exception ex) when (ex is NpgsqlException or RetryLimitExceededException && attempt < maxAttempts)
        {
            logger.LogWarning("Migration attempt {Attempt} of {MaxAttempts} failed: {Message}", attempt, maxAttempts, ex.Message);
            await Task.Delay(TimeSpan.FromSeconds(2));
        }
    }

    using var optionsScope = host.Services.CreateScope();
    var options = optionsScope.ServiceProvider.GetRequiredService<DbContextOptions<BatchDbContext>>();
    using var producer = new ProducerBuilder<string, string>(new ProducerConfig
    {
        BootstrapServers = bootstrapServers,
        Acks = Acks.All,
        EnableIdempotence = true,
        MessageTimeoutMs = 10000
    }).Build();

    var result = await DlqReprocessor.RunAsync(
        () => new BatchDbContext(options),
        new ConsumerConfig { BootstrapServers = bootstrapServers }, producer, logger, CancellationToken.None);
    producer.Flush(TimeSpan.FromSeconds(5));

    Console.WriteLine($"Reprocessed: {result.Reprocessed}, failed again: {result.FailedAgain}");
    return result.FailedAgain == 0 ? 0 : 1;
}

public partial class Program;
