using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace GridWise.Gateway.IntegrationTests;

[Collection("gateway")]
public class RateLimitingTests(GatewayFixture gateway)
{
    private static StringContent Calculation() => new("""{"consumptionKwh":250,"taxGroup":"household"}""", Encoding.UTF8, "application/json");

    private static async Task<List<HttpStatusCode>> Send(int times, Func<Task<HttpResponseMessage>> send)
    {
        var codes = new List<HttpStatusCode>();
        for (var i = 0; i < times; i++)
            codes.Add((await send()).StatusCode);
        return codes;
    }

    [Fact]
    public async Task The_21st_recommendation_in_a_window_is_429_with_retry_after_and_the_next_window_passes()
    {
        var client = gateway.CreateClient();

        var first20 = await Send(20, () => client.PostAsync("/api/recommendation/recommendations", Calculation()));
        var refused = await client.PostAsync("/api/recommendation/recommendations", Calculation());

        Assert.All(first20, code => Assert.Equal(HttpStatusCode.OK, code));
        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        var retryAfter = int.Parse(Assert.Single(refused.Headers.GetValues("Retry-After")));
        Assert.InRange(retryAfter, 1, 3);
        Assert.Equal("application/problem+json", refused.Content.Headers.ContentType?.MediaType);
        var problem = await refused.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(429, problem.GetProperty("status").GetInt32());
        Assert.Contains("recommendations", problem.GetProperty("detail").GetString());

        await Task.Delay(TimeSpan.FromSeconds(retryAfter) + TimeSpan.FromMilliseconds(300));
        var nextWindow = await client.PostAsync("/api/recommendation/recommendations", Calculation());
        Assert.Equal(HttpStatusCode.OK, nextWindow.StatusCode);
    }

    [Fact]
    public async Task Two_users_have_separate_limits()
    {
        var alice = gateway.CreateClient();
        var bob = gateway.CreateClient();

        await Send(20, () => alice.PostAsync("/api/recommendation/recommendations", Calculation()));
        var aliceAgain = await alice.PostAsync("/api/recommendation/recommendations", Calculation());
        var bobFirst = await bob.PostAsync("/api/recommendation/recommendations", Calculation());

        Assert.Equal(HttpStatusCode.TooManyRequests, aliceAgain.StatusCode);
        Assert.Equal(HttpStatusCode.OK, bobFirst.StatusCode);
    }

    [Fact]
    public async Task The_6th_upload_in_a_minute_is_429()
    {
        var client = gateway.CreateClient();
        HttpContent Upload() => new MultipartFormDataContent { { new ByteArrayContent("customer_id,consumption_kwh,tax_group\n"u8.ToArray()), "file", "c.csv" } };

        var codes = await Send(6, () => client.PostAsync("/api/batch/jobs", Upload()));

        Assert.Equal([.. Enumerable.Repeat(HttpStatusCode.OK, 5), HttpStatusCode.TooManyRequests], codes);
        // Other batch calls of the same user are under the general limit, not the upload limit.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/batch/jobs")).StatusCode);
    }

    [Fact]
    public async Task Other_routes_share_the_general_limit_of_100_per_window()
    {
        var client = gateway.CreateClient();

        // Recommendation GETs are not the calculation: they count against the general limit, together with catalog and batch.
        var codes = new List<HttpStatusCode>();
        codes.AddRange(await Send(40, () => client.GetAsync("/api/recommendation/recommendations/0b9c")));
        codes.AddRange(await Send(40, () => client.GetAsync("/api/catalog/plans")));
        codes.AddRange(await Send(20, () => client.GetAsync("/api/batch/jobs")));
        var refused = await client.GetAsync("/api/catalog/tax-groups");

        Assert.All(codes, code => Assert.Equal(HttpStatusCode.OK, code));
        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        Assert.Contains("general", await refused.Content.ReadAsStringAsync());
        Assert.True(refused.Headers.Contains("Retry-After"));
    }

    [Fact]
    public async Task Health_is_never_limited()
    {
        var client = gateway.CreateAnonymousClient();

        var codes = await Send(250, () => client.GetAsync("/health/ready"));

        Assert.All(codes, code => Assert.Equal(HttpStatusCode.OK, code));
    }
}
