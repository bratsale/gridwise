using GridWise.Recommendation.Api;
using GridWise.Recommendation.Data;
using GridWise.Recommendation.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

foreach (var setting in new[] { "Kafka:BootstrapServers", "Kafka:GroupId" })
{
    if (string.IsNullOrEmpty(builder.Configuration[setting]))
        throw new InvalidOperationException($"{setting} is not configured.");
}

builder.Services.AddDbContext<RecommendationDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Recommendation"),
            npgsql => npgsql.EnableRetryOnFailure())
        .UseSnakeCaseNamingConvention());

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddProblemDetails();
builder.Services.AddValidation();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks().AddDbContextCheck<RecommendationDbContext>(tags: ["ready"]);
builder.Services.AddScoped<RecommendationService>();
builder.Services.AddSingleton<CatalogEventHandler>();
builder.Services.AddHostedService<CatalogConsumer>();
builder.Services.AddHostedService<OutboxPublisher>();
builder.Services.AddHostedService<RetentionCleanup>();

var app = builder.Build();

const int maxAttempts = 30;
for (var attempt = 1; ; attempt++)
{
    try
    {
        using var scope = app.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<RecommendationDbContext>().Database.Migrate();
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

app.MapRecommendationEndpoints();

app.Run();

public partial class Program;
