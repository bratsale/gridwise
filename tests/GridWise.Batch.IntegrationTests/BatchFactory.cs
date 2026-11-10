using GridWise.Batch.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using Testcontainers.PostgreSql;

namespace GridWise.Batch.IntegrationTests;

public class BatchFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 11, 3, 10, 0, 0, TimeSpan.Zero));

    public Task InitializeAsync() => _postgres.StartAsync();

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        TestAuth.Configure(builder);
        builder.ConfigureAppConfiguration((_, config) =>
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Batch"] = _postgres.GetConnectionString(),
                ["Kafka:BootstrapServers"] = "unused:9092"
            }));

        builder.ConfigureServices(services =>
        {
            foreach (var hosted in new[] { typeof(BatchDispatcher), typeof(OutboxPublisher), typeof(RetentionCleanup), typeof(BatchResultsConsumer) })
                services.Remove(services.Single(d =>
                    d.ServiceType == typeof(IHostedService) && d.ImplementationType == hosted));

            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Time);
        });
    }

    // Every client of this factory sends a token, by default as admin "admin-1"; tests switch with .As(...) or .Anonymous().
    protected override void ConfigureClient(HttpClient client)
    {
        base.ConfigureClient(client);
        client.As(TestAuth.Admin, "admin-1");
    }
}
