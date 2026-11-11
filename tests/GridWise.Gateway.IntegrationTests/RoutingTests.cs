using System.Net;
using System.Net.Http.Json;
using System.Text;

namespace GridWise.Gateway.IntegrationTests;

[Collection("gateway")]
public class RoutingTests(GatewayFixture gateway)
{
    [Theory]
    [InlineData("/api/catalog/plans", "catalog", "/plans")]
    [InlineData("/api/catalog/tax-groups/household/versions", "catalog", "/tax-groups/household/versions")]
    [InlineData("/api/recommendation/recommendations/0b9c/accept", "recommendation", "/recommendations/0b9c/accept")]
    [InlineData("/api/batch/jobs", "batch", "/jobs")]
    [InlineData("/api/batch/jobs/42/results.csv", "batch", "/jobs/42/results.csv")]
    public async Task Each_prefix_reaches_its_service_with_the_prefix_removed(string path, string upstream, string upstreamPath)
    {
        var echo = await gateway.CreateClient().GetFromJsonAsync<Echo>(path);

        Assert.Equal(upstream, echo!.Upstream);
        Assert.Equal(upstreamPath, echo.Path);
    }

    // Our route transforms only remove the prefix: the method, the query string and the body arrive as they were sent.
    [Fact]
    public async Task Method_query_string_and_body_reach_the_service_unchanged()
    {
        const string json = """{"consumptionKwh":250,"taxGroup":"household"}""";

        var response = await gateway.CreateClient().PostAsync("/api/recommendation/recommendations?dryRun=true&x=a%20b",
            new StringContent(json, Encoding.UTF8, "application/json"));
        var echo = await response.Content.ReadFromJsonAsync<Echo>();

        Assert.Equal(("recommendation", "POST", "/recommendations", "?dryRun=true&x=a%20b", json),
            (echo!.Upstream, echo.Method, echo.Path, echo.Query, echo.Body));
    }

    [Fact]
    public async Task An_upload_of_10_MB_reaches_batch()
    {
        var file = new byte[10 * 1024 * 1024];
        var form = new MultipartFormDataContent { { new ByteArrayContent(file), "file", "customers.csv" } };

        var response = await gateway.CreateClient().PostAsync("/api/batch/jobs", form);
        var echo = await response.Content.ReadFromJsonAsync<Echo>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("batch", echo!.Upstream);
        Assert.True(echo.BodyLength > file.Length); // the file plus the multipart framing
    }

    [Fact]
    public async Task An_upload_over_11_MB_is_refused_by_the_gateway()
    {
        var form = new MultipartFormDataContent { { new ByteArrayContent(new byte[12 * 1024 * 1024]), "file", "big.csv" } };
        // With "Expect: 100-continue" the client waits for the gateway's answer before sending the body (as a browser upload
        // through nginx effectively does); without it, the gateway answers 413 while the client is still writing.
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/batch/jobs") { Content = form };
        request.Headers.ExpectContinue = true;

        var response = await gateway.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Fact]
    public async Task Server_sent_events_are_streamed_not_buffered()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/batch/jobs/7/events");
        using var response = await gateway.CreateClient().SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync());

        // The upstream has written only the first event and is waiting: it must already be here.
        var first = await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("data: first", first);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);

        gateway.Batch.ReleaseStream();
        var rest = await reader.ReadToEndAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Contains("data: second", rest);
    }
}
