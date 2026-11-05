using GridWise.Catalog.Api;
using GridWise.Catalog.Data;
using GridWise.Catalog.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

if (string.IsNullOrEmpty(builder.Configuration["Kafka:BootstrapServers"]))
    throw new InvalidOperationException("Kafka:BootstrapServers is not configured.");

builder.Services.AddDbContext<CatalogDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Catalog"),
            npgsql => npgsql.EnableRetryOnFailure())
        .UseSnakeCaseNamingConvention());

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddProblemDetails();
builder.Services.AddValidation();
builder.Services.AddOpenApi();
builder.Services.AddScoped<PlanService>();
builder.Services.AddScoped<TaxGroupService>();
builder.Services.AddHostedService<IdempotencyKeyCleanup>();
builder.Services.AddHostedService<OutboxPublisher>();

var app = builder.Build();

const int maxAttempts = 30;
for (var attempt = 1; ; attempt++)
{
    try
    {
        using var scope = app.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<CatalogDbContext>().Database.Migrate();
        break;
    }
    catch (Exception ex) when (ex is NpgsqlException or RetryLimitExceededException && attempt < maxAttempts)
    {
        app.Logger.LogWarning("Migration attempt {Attempt} of {MaxAttempts} failed: {Message}", attempt, maxAttempts, ex.Message);
        await Task.Delay(TimeSpan.FromSeconds(2));
    }
}

app.MapOpenApi();
if (app.Environment.IsDevelopment())
    app.MapScalarApiReference();

app.MapPlanEndpoints();
app.MapTaxGroupEndpoints();

app.Run();

public partial class Program;
