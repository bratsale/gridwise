using System.Net.Http.Headers;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace GridWise.Batch.IntegrationTests;

/// <summary>
/// Tokens for tests, without Keycloak: the service under test trusts a key made here, and <see cref="Token"/> signs tokens like
/// Keycloak's (issuer, audience gridwise-api, the role in realm_access.roles, the user in sub). They are valid from 2020 to 2099,
/// so a fake clock in a test cannot make them expired.
/// </summary>
public static class TestAuth
{
    public const string Issuer = "http://localhost:5100/auth/realms/gridwise";
    public const string Admin = "admin";
    public const string Customer = "customer";

    private static readonly RsaSecurityKey Key = new(RSA.Create(2048)) { KeyId = "test-key" };

    public static string Token(string role, string sub) => new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
    {
        Issuer = Issuer,
        IssuedAt = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        NotBefore = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        Expires = new DateTime(2099, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        Claims = new Dictionary<string, object>
        {
            ["aud"] = new[] { "gridwise-api", "account" },
            ["sub"] = sub,
            ["preferred_username"] = sub,
            ["realm_access"] = new Dictionary<string, object> { ["roles"] = new[] { role, "default-roles-gridwise" } }
        },
        SigningCredentials = new SigningCredentials(Key, SecurityAlgorithms.RsaSha256)
    });

    /// <summary>Makes the service trust the test key instead of fetching Keycloak's.</summary>
    public static void Configure(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:MetadataAddress"] = "http://unused.test/.well-known/openid-configuration",
            ["Jwt:RequireHttpsMetadata"] = "false",
            ["Jwt:Issuer"] = Issuer,
            ["Jwt:Audience"] = "gridwise-api"
        }));
        builder.ConfigureTestServices(services =>
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
                options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(
                    new OpenIdConnectConfiguration { Issuer = Issuer, SigningKeys = { Key } })));
    }

    /// <summary>The client sends a token for this role and user from now on.</summary>
    public static HttpClient As(this HttpClient client, string role, string sub)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token(role, sub));
        return client;
    }

    /// <summary>The client sends no token.</summary>
    public static HttpClient Anonymous(this HttpClient client)
    {
        client.DefaultRequestHeaders.Authorization = null;
        return client;
    }
}
