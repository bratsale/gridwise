using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;

namespace GridWise.Gateway.IntegrationTests;

[Collection("gateway")]
public class AuthenticationTests(GatewayFixture gateway)
{
    private Task<HttpResponseMessage> GetWithToken(string path, string token)
    {
        var client = gateway.CreateAnonymousClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client.GetAsync(path);
    }

    private static async Task AssertUnauthorizedProblem(HttpResponseMessage response, bool invalidToken)
    {
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var challenge = Assert.Single(response.Headers.WwwAuthenticate);
        Assert.Equal("Bearer", challenge.Scheme);
        Assert.Equal(invalidToken, challenge.Parameter?.Contains("error=\"invalid_token\"") == true);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(401, problem.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task No_token_is_401_with_problem_details_and_a_bearer_challenge()
    {
        var response = await gateway.CreateAnonymousClient().GetAsync("/api/catalog/plans");

        await AssertUnauthorizedProblem(response, invalidToken: false);
    }

    public static TheoryData<string, string> InvalidTokens => new()
    {
        { "wrong issuer", GatewayFixture.Token(issuer: "http://evil.example/realms/gridwise") },
        { "wrong audience", GatewayFixture.Token(audiences: ["account"]) },
        { "expired", GatewayFixture.Token(expiresIn: TimeSpan.FromMinutes(-2)) },
        { "bad signature", GatewayFixture.Token(key: new RsaSecurityKey(RSA.Create(2048)) { KeyId = GatewayFixture.SigningKey.KeyId }) }
    };

    [Theory]
    [MemberData(nameof(InvalidTokens))]
    public async Task An_invalid_token_is_401(string _, string token)
    {
        var response = await GetWithToken("/api/catalog/plans", token);

        await AssertUnauthorizedProblem(response, invalidToken: true);
    }

    [Fact]
    public async Task A_token_that_expired_within_the_clock_skew_is_accepted()
    {
        var response = await GetWithToken("/api/catalog/plans", GatewayFixture.Token(expiresIn: TimeSpan.FromSeconds(-10)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_valid_token_is_proxied_and_the_authorization_header_reaches_the_service_unchanged()
    {
        var token = GatewayFixture.Token();

        var response = await GetWithToken("/api/catalog/plans", token);
        var echo = await response.Content.ReadFromJsonAsync<Echo>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(("catalog", "/plans"), (echo!.Upstream, echo.Path));
        Assert.Equal($"Bearer {token}", echo.Authorization);
    }

    [Fact]
    public async Task The_events_route_accepts_the_token_in_the_query_and_forwards_it_as_a_header_without_access_token()
    {
        var token = GatewayFixture.Token();
        var jobId = Guid.NewGuid();

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/batch/jobs/{jobId}/events?access_token={token}&lastEventId=3");
        using var response = await gateway.CreateAnonymousClient().SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync());
        Assert.Equal("data: first", await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5)));
        gateway.Batch.ReleaseStream();

        var seen = gateway.Batch.Received.Last(e => e.Path == $"/jobs/{jobId}/events");
        Assert.Equal($"Bearer {token}", seen.Authorization);
        Assert.DoesNotContain("access_token", seen.Query);
        Assert.Equal("?lastEventId=3", seen.Query); // other parameters still pass
    }

    [Theory]
    [InlineData("/api/batch/jobs")]
    [InlineData("/api/catalog/plans")]
    [InlineData("/api/batch/jobs/42/results.csv")]
    public async Task The_query_token_is_refused_on_every_other_route(string path)
    {
        var response = await gateway.CreateAnonymousClient().GetAsync($"{path}?access_token={GatewayFixture.Token()}");

        await AssertUnauthorizedProblem(response, invalidToken: false);
    }

    [Fact]
    public async Task The_query_token_never_appears_in_logs_or_spans()
    {
        var token = GatewayFixture.Token();
        var jobId = Guid.NewGuid();

        using (var response = await gateway.CreateAnonymousClient().GetAsync(
                   $"/api/batch/jobs/{jobId}/events?access_token={token}", HttpCompletionOption.ResponseHeadersRead))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            gateway.Batch.ReleaseStream();
            await response.Content.ReadAsStringAsync();
        }
        // The server span ends just after the response: wait until it is recorded instead of pausing for a fixed time.
        System.Diagnostics.Activity? serverSpan;
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while ((serverSpan = gateway.Spans.LastOrDefault(s => s.Kind == System.Diagnostics.ActivityKind.Server
                   && (s.GetTagItem("url.path") as string) == $"/api/batch/jobs/{jobId}/events")) is null)
        {
            Assert.True(DateTime.UtcNow < deadline, "The server span of the events request was not recorded.");
            await Task.Delay(50);
        }
        Assert.Equal("?access_token=REDACTED", serverSpan.GetTagItem("url.query"));

        Assert.DoesNotContain(gateway.Logs, line => line.Contains(token));
        Assert.DoesNotContain(gateway.Spans, span =>
            span.DisplayName.Contains(token) || span.TagObjects.Any(tag => tag.Value?.ToString()?.Contains(token) == true));
    }
}
