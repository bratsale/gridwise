using System.Net;
using System.Net.Http.Json;
using GridWise.Catalog.Api;

namespace GridWise.Catalog.IntegrationTests;

public class PlanQueryTests(CatalogFactory factory) : IClassFixture<CatalogFactory>
{
    private static object StandardPlan(string name = "Standard") => new
    {
        name,
        discount = 0.05m,
        tiers = new object[]
        {
            new { threshold = 100m, pricePerKwh = 0.10m },
            new { threshold = 300m, pricePerKwh = 0.08m },
            new { threshold = 500m, pricePerKwh = 0.07m },
            new { threshold = (decimal?)null, pricePerKwh = 0.06m }
        }
    };

    private Task<HttpResponseMessage> Send(HttpMethod method, string url, object? body = null, string? ifMatch = null)
    {
        var request = new HttpRequestMessage(method, url);
        if (body is not null)
            request.Content = JsonContent.Create(body);
        if (ifMatch is not null)
            request.Headers.TryAddWithoutValidation("If-Match", ifMatch);

        return factory.CreateClient().SendAsync(request);
    }

    private async Task<PlanResponse> CreatePlan()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/plans") { Content = JsonContent.Create(StandardPlan()) };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));

        var response = await factory.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PlanResponse>())!;
    }

    private static async Task<T> Read<T>(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    [Fact]
    public async Task Get_plan_returns_latest_version_with_etag()
    {
        var created = await CreatePlan();

        var first = await Send(HttpMethod.Get, $"/plans/{created.Id}");

        Assert.Equal("\"1\"", first.Headers.ETag?.Tag);
        var plan = await Read<PlanResponse>(first);
        Assert.Equal(created.Id, plan.Id);
        Assert.Equal(1, plan.VersionNo);
        Assert.Equal(created.Name, plan.Name);
        Assert.Equal(created.Discount, plan.Discount);
        Assert.Equal("Active", plan.Status);
        Assert.Equal(created.EffectiveFrom, plan.EffectiveFrom);
        Assert.Equal(created.Tiers, plan.Tiers);

        await Send(HttpMethod.Post, $"/plans/{created.Id}/versions", StandardPlan("Standard v2"), "\"1\"");

        var second = await Send(HttpMethod.Get, $"/plans/{created.Id}");

        Assert.Equal("\"2\"", second.Headers.ETag?.Tag);
        var latest = await Read<PlanResponse>(second);
        Assert.Equal(2, latest.VersionNo);
        Assert.Equal("Standard v2", latest.Name);
    }

    [Fact]
    public async Task List_versions_returns_all_versions_in_order()
    {
        var created = await CreatePlan();
        await Send(HttpMethod.Post, $"/plans/{created.Id}/versions", StandardPlan("Standard v2"), "\"1\"");

        var versions = await Read<List<PlanResponse>>(await Send(HttpMethod.Get, $"/plans/{created.Id}/versions"));

        Assert.Equal([1, 2], versions.Select(v => v.VersionNo));
        Assert.Equal(["Standard", "Standard v2"], versions.Select(v => v.Name));
    }

    [Fact]
    public async Task Get_version_returns_that_version()
    {
        var created = await CreatePlan();
        await Send(HttpMethod.Post, $"/plans/{created.Id}/versions", StandardPlan("Standard v2"), "\"1\"");

        var version = await Read<PlanResponse>(await Send(HttpMethod.Get, $"/plans/{created.Id}/versions/1"));

        Assert.Equal(1, version.VersionNo);
        Assert.Equal("Standard", version.Name);
        Assert.Equal(created.Tiers, version.Tiers);
    }

    [Fact]
    public async Task Unknown_version_is_not_found()
    {
        var created = await CreatePlan();

        var response = await Send(HttpMethod.Get, $"/plans/{created.Id}/versions/9");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/versions")]
    [InlineData("/versions/1")]
    public async Task Unknown_plan_is_not_found(string suffix)
    {
        var response = await Send(HttpMethod.Get, $"/plans/{Guid.NewGuid()}{suffix}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task List_plans_hides_retired_plans_unless_requested()
    {
        var active = await CreatePlan();
        var retired = await CreatePlan();
        await Send(HttpMethod.Delete, $"/plans/{retired.Id}", ifMatch: "\"1\"");

        var defaultList = await Read<List<PlanResponse>>(await Send(HttpMethod.Get, "/plans"));
        var fullList = await Read<List<PlanResponse>>(await Send(HttpMethod.Get, "/plans?includeRetired=true"));

        Assert.Contains(defaultList, p => p.Id == active.Id);
        Assert.DoesNotContain(defaultList, p => p.Id == retired.Id);
        Assert.Contains(fullList, p => p.Id == active.Id);
        var listed = Assert.Single(fullList, p => p.Id == retired.Id);
        Assert.Equal("Retired", listed.Status);
        Assert.Equal(2, listed.VersionNo);
    }

    [Fact]
    public async Task OpenApi_document_describes_the_plan_routes()
    {
        var response = await Send(HttpMethod.Get, "/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("/plans/{id}/versions", await response.Content.ReadAsStringAsync());
    }
}
