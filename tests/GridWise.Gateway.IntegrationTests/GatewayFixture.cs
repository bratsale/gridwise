using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace GridWise.Gateway.IntegrationTests;

/// <summary>Three stub services and the gateway (on real Kestrel) pointed at them through configuration.</summary>
public sealed class GatewayFixture : IAsyncLifetime
{
    public StubUpstream Catalog { get; private set; } = null!;
    public StubUpstream Recommendation { get; private set; } = null!;
    public StubUpstream Batch { get; private set; } = null!;
    public WebApplicationFactory<Program> Gateway { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        Catalog = await StubUpstream.StartAsync("catalog");
        Recommendation = await StubUpstream.StartAsync("recommendation");
        Batch = await StubUpstream.StartAsync("batch");

        Gateway = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ReverseProxy:Clusters:catalog:Destinations:main:Address"] = Catalog.Address,
                ["ReverseProxy:Clusters:recommendation:Destinations:main:Address"] = Recommendation.Address,
                ["ReverseProxy:Clusters:batch:Destinations:main:Address"] = Batch.Address
            })));
        Gateway.UseKestrel(0);
        Gateway.StartServer();
    }

    public HttpClient CreateClient() => Gateway.CreateClient();

    public async Task DisposeAsync()
    {
        await Gateway.DisposeAsync();
        await Catalog.DisposeAsync();
        await Recommendation.DisposeAsync();
        await Batch.DisposeAsync();
    }
}

// One gateway and one set of stubs for all test classes: starting two WebApplicationFactory hosts of the same Program
// at the same time is not reliable, and the tests do not change anything the others see.
[CollectionDefinition("gateway")]
public class GatewayCollection : ICollectionFixture<GatewayFixture>;
