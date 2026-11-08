using GridWise.Batch.Api;
using GridWise.Batch.Data;
using GridWise.Batch.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Scalar.AspNetCore;

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

public partial class Program;
