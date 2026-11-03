using System.Net;
using System.Net.Http.Json;
using GridWise.Catalog.Api;
using GridWise.Catalog.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GridWise.Catalog.IntegrationTests;

[Collection("catalog")]
public class PostPlanTests(CatalogFactory factory)
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

    private static string NewKey() => Guid.NewGuid().ToString("N");

    private Task<HttpResponseMessage> Post(object body, string? idempotencyKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/plans") { Content = JsonContent.Create(body) };
        if (idempotencyKey is not null)
            request.Headers.Add("Idempotency-Key", idempotencyKey);

        return factory.CreateClient().SendAsync(request);
    }

    private static async Task<PlanResponse> ReadPlan(HttpResponseMessage response)
        => (await response.Content.ReadFromJsonAsync<PlanResponse>())!;

    private async Task<T> InDb<T>(Func<CatalogDbContext, Task<T>> query)
    {
        using var scope = factory.Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<CatalogDbContext>());
    }

    [Fact]
    public async Task Valid_plan_is_created()
    {
        var response = await Post(StandardPlan(), NewKey());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var plan = await ReadPlan(response);
        Assert.Equal($"/plans/{plan.Id}", response.Headers.Location?.ToString());
        Assert.Equal("\"1\"", response.Headers.ETag?.Tag);

        Assert.Equal(1, await InDb(db => db.Plans.CountAsync(p => p.Id == plan.Id)));
        Assert.Equal(1, await InDb(db => db.PlanVersions.CountAsync(v => v.PlanId == plan.Id)));
        Assert.Equal(4, await InDb(db => db.Set<GridWise.Catalog.Domain.PlanTier>().CountAsync(t => t.PlanId == plan.Id)));
        var outbox = await InDb(db => db.OutboxMessages.Where(m => m.AggregateId == plan.Id.ToString()).ToListAsync());
        Assert.Equal("PlanVersionCreated", Assert.Single(outbox).EventType);
    }

    [Fact]
    public async Task Same_key_and_body_twice_returns_same_plan()
    {
        var key = NewKey();

        var first = await Post(StandardPlan(), key);
        var second = await Post(StandardPlan(), key);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        var id = (await ReadPlan(first)).Id;
        Assert.Equal(id, (await ReadPlan(second)).Id);

        Assert.Equal(1, await InDb(db => db.Plans.CountAsync(p => p.Id == id)));
        Assert.Equal(1, await InDb(db => db.OutboxMessages.CountAsync(m => m.AggregateId == id.ToString())));
    }

    [Fact]
    public async Task Same_key_with_different_body_is_rejected()
    {
        var key = NewKey();

        await Post(StandardPlan(), key);
        var second = await Post(StandardPlan("Other"), key);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, second.StatusCode);
    }

    [Fact]
    public async Task Missing_idempotency_key_is_rejected()
    {
        var response = await Post(StandardPlan(), null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Invalid_tiers_are_rejected_and_nothing_is_stored()
    {
        var key = NewKey();
        var body = new
        {
            name = "Standard",
            discount = 0.05m,
            tiers = new[]
            {
                new { threshold = (decimal?)100m, pricePerKwh = 0.10m },
                new { threshold = (decimal?)300m, pricePerKwh = 0.08m },
                new { threshold = (decimal?)500m, pricePerKwh = 0.07m },
                new { threshold = (decimal?)600m, pricePerKwh = 0.06m }
            }
        };

        var response = await Post(body, key);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>();
        Assert.Contains("tiers[3].threshold", problem!.Errors.Keys);
        Assert.Equal(0, await InDb(db => db.IdempotencyKeys.CountAsync(k => k.Key == key)));
    }

    [Fact]
    public async Task Effective_date_in_the_past_is_rejected()
    {
        var body = new
        {
            name = "Standard",
            discount = 0.05m,
            effectiveFrom = "2026-11-02",
            tiers = new[] { new { threshold = (decimal?)null, pricePerKwh = 0.06m } }
        };

        var response = await Post(body, NewKey());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>();
        Assert.Contains("effectiveFrom", problem!.Errors.Keys);
    }

    [Fact]
    public async Task Parallel_requests_with_same_key_create_one_plan()
    {
        var key = NewKey();

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => Post(StandardPlan(), key)));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));
        var ids = (await Task.WhenAll(responses.Select(ReadPlan))).Select(p => p.Id).Distinct().ToList();
        var id = Assert.Single(ids);

        Assert.Equal(1, await InDb(db => db.Plans.CountAsync(p => p.Id == id)));
        Assert.Equal(1, await InDb(db => db.OutboxMessages.CountAsync(m => m.AggregateId == id.ToString())));
    }
}
