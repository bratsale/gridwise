using GridWise.Recommendation.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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
            services.Remove(services.Single(d =>
                d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(CatalogConsumer))));
    }
}
