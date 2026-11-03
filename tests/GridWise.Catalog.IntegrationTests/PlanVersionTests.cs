using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GridWise.Catalog.Api;
using GridWise.Catalog.Data;
using GridWise.Catalog.Domain;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GridWise.Catalog.IntegrationTests;

public class PlanVersionTests(CatalogFactory factory) : IClassFixture<CatalogFactory>
{
    private static object StandardPlan(string name = "Standard", string? effectiveFrom = null) => new
    {
        name,
        discount = 0.05m,
        effectiveFrom,
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

    private async Task<PlanResponse> CreatePlan(string? effectiveFrom = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/plans") { Content = JsonContent.Create(StandardPlan(effectiveFrom: effectiveFrom)) };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));

        var response = await factory.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PlanResponse>())!;
    }

    private Task<HttpResponseMessage> AddVersion(Guid planId, string? ifMatch, object? body = null)
        => Send(HttpMethod.Post, $"/plans/{planId}/versions", body ?? StandardPlan("Standard v2"), ifMatch);

    private Task<HttpResponseMessage> Retire(Guid planId, string? ifMatch, string? effectiveFrom = null)
        => Send(HttpMethod.Delete, $"/plans/{planId}" + (effectiveFrom is null ? "" : $"?effectiveFrom={effectiveFrom}"), ifMatch: ifMatch);

    private async Task<T> InDb<T>(Func<CatalogDbContext, Task<T>> query)
    {
        using var scope = factory.Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<CatalogDbContext>());
    }

    private Task<List<PlanVersion>> Versions(Guid planId)
        => InDb(db => db.PlanVersions.Where(v => v.PlanId == planId).OrderBy(v => v.VersionNo).ToListAsync());

    private Task<List<OutboxMessage>> Outbox(Guid planId)
        => InDb(db => db.OutboxMessages.Where(m => m.AggregateId == planId.ToString()).ToListAsync());

    [Fact]
    public async Task New_version_is_created()
    {
        var plan = await CreatePlan();

        var response = await AddVersion(plan.Id, "\"1\"");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("\"2\"", response.Headers.ETag?.Tag);
        Assert.Equal($"/plans/{plan.Id}/versions/2", response.Headers.Location?.ToString());
        Assert.Equal([1, 2], (await Versions(plan.Id)).Select(v => v.VersionNo));
        Assert.Equal(2, (await Outbox(plan.Id)).Count);
    }

    [Fact]
    public async Task New_version_with_stale_if_match_conflicts()
    {
        var plan = await CreatePlan();
        await AddVersion(plan.Id, "\"1\"");

        var response = await AddVersion(plan.Id, "\"1\"");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("abc")]
    public async Task New_version_with_missing_or_invalid_if_match_is_rejected(string? ifMatch)
    {
        var plan = await CreatePlan();

        var response = await AddVersion(plan.Id, ifMatch);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task New_version_for_unknown_plan_is_not_found()
    {
        var response = await AddVersion(Guid.NewGuid(), "\"1\"");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task New_version_effective_before_previous_version_is_rejected()
    {
        var plan = await CreatePlan(effectiveFrom: "2026-12-01");

        var response = await AddVersion(plan.Id, "\"1\"", StandardPlan("Standard v2", effectiveFrom: "2026-11-15"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>();
        Assert.Contains("effectiveFrom", problem!.Errors.Keys);
    }

    [Fact]
    public async Task Retire_adds_a_retired_version_without_tiers()
    {
        var plan = await CreatePlan();

        var response = await Retire(plan.Id, "\"1\"");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("\"2\"", response.Headers.ETag?.Tag);

        var versions = await Versions(plan.Id);
        var retired = versions[1];
        Assert.Equal(VersionStatus.Retired, retired.Status);
        Assert.Equal(versions[0].Name, retired.Name);
        Assert.Equal(versions[0].Discount, retired.Discount);
        Assert.Equal(0, await InDb(db => db.Set<PlanTier>().CountAsync(t => t.PlanId == plan.Id && t.VersionNo == 2)));

        var payloads = (await Outbox(plan.Id)).Select(m => JsonDocument.Parse(m.Payload).RootElement).ToList();
        Assert.Contains(payloads, p => p.GetProperty("versionNo").GetInt32() == 2 && p.GetProperty("status").GetString() == "Retired");
    }

    [Fact]
    public async Task Retired_plan_rejects_new_versions_and_second_retire()
    {
        var plan = await CreatePlan();
        await Retire(plan.Id, "\"1\"");

        var newVersion = await AddVersion(plan.Id, "\"2\"");
        var retireAgain = await Retire(plan.Id, "\"2\"");

        foreach (var response in new[] { newVersion, retireAgain })
        {
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
            Assert.Contains("retired", problem!.Detail);
        }
    }

    [Fact]
    public async Task Retire_with_past_effective_date_is_rejected()
    {
        var plan = await CreatePlan();

        var response = await Retire(plan.Id, "\"1\"", effectiveFrom: "2026-11-02");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Retire_today_after_a_future_version_is_rejected()
    {
        var plan = await CreatePlan();
        var future = await AddVersion(plan.Id, "\"1\"", StandardPlan("Standard v2", effectiveFrom: "2026-12-01"));
        Assert.Equal(HttpStatusCode.Created, future.StatusCode);

        var response = await Retire(plan.Id, "\"2\"");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>();
        Assert.Contains("effectiveFrom", problem!.Errors.Keys);
    }

    [Fact]
    public async Task Parallel_new_versions_with_same_if_match_one_wins()
    {
        var plan = await CreatePlan();

        var responses = await Task.WhenAll(AddVersion(plan.Id, "\"1\""), AddVersion(plan.Id, "\"1\""));

        Assert.Equal(
            [HttpStatusCode.Created, HttpStatusCode.Conflict],
            responses.Select(r => r.StatusCode).Order());
        Assert.Equal([1, 2], (await Versions(plan.Id)).Select(v => v.VersionNo));
        Assert.Equal(2, (await Outbox(plan.Id)).Count);
    }
}
