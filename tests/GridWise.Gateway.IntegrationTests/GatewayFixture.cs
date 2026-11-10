using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace GridWise.Gateway.IntegrationTests;

/// <summary>
/// Three stub services and the gateway (on real Kestrel) pointed at them through configuration. Instead of Keycloak, the gateway
/// trusts a key made here, and <see cref="Token"/> signs tokens with it. Everything the gateway logs and every span it ends are
/// kept, so tests can check that a token never shows up in them.
/// </summary>
public sealed class GatewayFixture : IAsyncLifetime
{
    public const string Issuer = "http://localhost:5100/auth/realms/gridwise";
    public static readonly RsaSecurityKey SigningKey = new(RSA.Create(2048)) { KeyId = "test-key" };

    private ActivityListener? _spans;

    public StubUpstream Catalog { get; private set; } = null!;
    public StubUpstream Recommendation { get; private set; } = null!;
    public StubUpstream Batch { get; private set; } = null!;
    public WebApplicationFactory<Program> Gateway { get; private set; } = null!;

    public ConcurrentQueue<string> Logs { get; } = new();
    public ConcurrentQueue<Activity> Spans { get; } = new();

    public async Task InitializeAsync()
    {
        Catalog = await StubUpstream.StartAsync("catalog");
        Recommendation = await StubUpstream.StartAsync("recommendation");
        Batch = await StubUpstream.StartAsync("batch");

        _spans = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = Spans.Enqueue
        };
        ActivitySource.AddActivityListener(_spans);

        Gateway = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ReverseProxy:Clusters:catalog:Destinations:main:Address"] = Catalog.Address,
                ["ReverseProxy:Clusters:recommendation:Destinations:main:Address"] = Recommendation.Address,
                ["ReverseProxy:Clusters:batch:Destinations:main:Address"] = Batch.Address,
                ["Jwt:MetadataAddress"] = "http://unused.test/.well-known/openid-configuration", // replaced by the static configuration below
                ["Jwt:RequireHttpsMetadata"] = "false",
                // The real limits, except a shorter recommendations window, so "the next window" is 3 s away instead of 10.
                ["RateLimiting:Recommendations:WindowSeconds"] = "3",
                // As verbose as it gets by default: the categories that would print a request line stay pinned in appsettings.json.
                ["Logging:LogLevel:Default"] = "Trace"
            }));
            builder.ConfigureLogging(logging => logging.AddProvider(new CapturingLoggerProvider(Logs)));
            builder.ConfigureTestServices(services =>
                services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
                    options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(
                        new OpenIdConnectConfiguration { Issuer = Issuer, SigningKeys = { SigningKey } })));
        });
        Gateway.UseKestrel(0);
        Gateway.StartServer();
    }

    /// <summary>A token like Keycloak's, by default valid for 5 minutes; each argument breaks one thing.</summary>
    public static string Token(
        string issuer = Issuer, string[]? audiences = null, TimeSpan? expiresIn = null, SecurityKey? key = null, string? sub = null)
    {
        var expires = DateTime.UtcNow + (expiresIn ?? TimeSpan.FromMinutes(5));
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            IssuedAt = expires.AddMinutes(-5),
            NotBefore = expires.AddMinutes(-5),
            Expires = expires,
            Claims = new Dictionary<string, object>
            {
                ["aud"] = audiences ?? ["gridwise-api", "account"],
                ["sub"] = sub ?? "0b6c8d1e-0000-0000-0000-000000000001",
                ["preferred_username"] = "admin",
                ["realm_access"] = new Dictionary<string, object> { ["roles"] = new[] { "admin" } }
            },
            SigningCredentials = new SigningCredentials(key ?? SigningKey, SecurityAlgorithms.RsaSha256)
        });
    }

    /// <summary>
    /// A client that sends a valid token on every request, by default for a user of its own, so the rate limits of one test
    /// never count the requests of another.
    /// </summary>
    public HttpClient CreateClient(string? sub = null)
    {
        var client = Gateway.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(sub: sub ?? Guid.NewGuid().ToString()));
        return client;
    }

    public HttpClient CreateAnonymousClient() => Gateway.CreateClient();

    public async Task DisposeAsync()
    {
        _spans?.Dispose();
        await Gateway.DisposeAsync();
        await Catalog.DisposeAsync();
        await Recommendation.DisposeAsync();
        await Batch.DisposeAsync();
    }

    private sealed class CapturingLoggerProvider(ConcurrentQueue<string> logs) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, logs);
        public void Dispose() { }
    }

    private sealed class CapturingLogger(string category, ConcurrentQueue<string> logs) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            logs.Enqueue($"{category} scope: {state}");
            return null;
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => logs.Enqueue($"{category} {logLevel}: {formatter(state, exception)} {exception}");
    }
}

// One gateway and one set of stubs for all test classes: starting two WebApplicationFactory hosts of the same Program
// at the same time is not reliable, and the tests do not change anything the others see.
[CollectionDefinition("gateway")]
public class GatewayCollection : ICollectionFixture<GatewayFixture>;
