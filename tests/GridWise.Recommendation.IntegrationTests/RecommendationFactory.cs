using GridWise.Recommendation.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using Testcontainers.PostgreSql;

namespace GridWise.Recommendation.IntegrationTests;

public class RecommendationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Recommendation"] = _postgres.GetConnectionString(),
                ["Kafka:BootstrapServers"] = "unused:9092",
                ["Kafka:GroupId"] = "unused"
            }));

        builder.ConfigureServices(services =>
        {
            services.AddScoped<BatchChunkProcessor>(); // the web host does not need it; the worker tests do
            services.RemoveAll<TimeProvider>();
            foreach (var hostedService in new[] { typeof(CatalogConsumer), typeof(OutboxPublisher), typeof(RetentionCleanup) })
                services.Remove(services.Single(d =>
                    d.ServiceType == typeof(IHostedService) && d.ImplementationType == hostedService));
            services.AddSingleton<TimeProvider>(new FakeTimeProvider(new DateTimeOffset(2026, 11, 5, 10, 0, 0, TimeSpan.Zero)));
        });
    }
}
