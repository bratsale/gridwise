using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GridWise.Recommendation.Api;
using GridWise.Recommendation.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GridWise.Recommendation.IntegrationTests;

[Collection("recommendation")]
public class RecommendationTests(RecommendationFactory factory) : IAsyncLifetime
{
    private const string StandardTiers = """[{"threshold":100,"pricePerKwh":0.10},{"threshold":300,"pricePerKwh":0.08},{"threshold":500,"pricePerKwh":0.07},{"threshold":null,"pricePerKwh":0.06}]""";
    private const string PremiumTiers = """[{"threshold":200,"pricePerKwh":0.09},{"threshold":400,"pricePerKwh":0.07},{"threshold":600,"pricePerKwh":0.05},{"threshold":null,"pricePerKwh":0.04}]""";

    // Other tests in this collection leave copy rows behind, and a recommendation prices every plan.
    public async Task InitializeAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecommendationDbContext>();
        await db.Database.ExecuteSqlRawAsync("DELETE FROM plan_versions");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM tax_group_versions");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task InsertPlan(
        Guid planId, int versionNo, string name, string status, string effectiveFrom, decimal discount, string tiers)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecommendationDbContext>();
        var from = DateOnly.Parse(effectiveFrom);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO plan_versions (plan_id, version_no, name, discount, status, effective_from, tiers, received_at)
            VALUES ({planId}, {versionNo}, {name}, {discount}, {status}, {from}, {tiers}::jsonb, {DateTimeOffset.UtcNow})
            """);
    }

    private async Task InsertHousehold()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecommendationDbContext>();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO tax_group_versions (code, version_no, vat, eco_tax, status, effective_from, received_at)
            VALUES ('household', 1, 0.17, 0.005, 'Active', '2026-01-01', {DateTimeOffset.UtcNow})
            """);
    }

    private Task<HttpResponseMessage> Post(object body, string? correlationId = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/recommendations") { Content = JsonContent.Create(body) };
        if (correlationId is not null)
            request.Headers.Add("X-Correlation-Id", correlationId);
        return factory.CreateClient().SendAsync(request);
    }

    private static object Household(decimal kwh) => new { consumptionKwh = kwh, taxGroup = "household" };

    private async Task SeedStandardAndPremium()
    {
        await InsertHousehold();
        await InsertPlan(Guid.NewGuid(), 1, "Standard", "Active", "2026-01-01", 0.05m, StandardTiers);
        await InsertPlan(Guid.NewGuid(), 1, "Premium", "Active", "2026-01-01", 0.10m, PremiumTiers);
    }

    [Fact]
    public async Task Cheapest_plan_is_recommended_with_full_breakdown()
    {
        await SeedStandardAndPremium();
        var correlationId = Guid.NewGuid().ToString();

        var response = await Post(Household(250m), correlationId);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = (await response.Content.ReadFromJsonAsync<RecommendationResponse>())!;
        Assert.Equal($"/recommendations/{body.Id}", response.Headers.Location?.ToString());
        Assert.Equal(correlationId, response.Headers.GetValues("X-Correlation-Id").Single());

        Assert.Equal("household", body.TaxGroup.Code);
        Assert.Equal(1, body.TaxGroup.VersionNo);
        Assert.Equal("Premium,Standard", string.Join(",", body.Plans.Select(p => p.Name)));
        Assert.Equal(24.10m, body.Plans[0].Total);
        Assert.Equal(25.92m, body.Plans[1].Total);
        Assert.Equal("Premium", body.Recommended!.Name);
        Assert.Equal(body.Plans[0].PlanId, body.Recommended.PlanId);

        var b = body.Recommended.Breakdown;
        Assert.Equal(21.50m, b.EnergySubtotal);
        Assert.Equal(2.15m, b.DiscountAmount);
        Assert.Equal(19.35m, b.NetEnergy);
        Assert.Equal(1.25m, b.EcoTax);
        Assert.Equal(20.60m, b.VatBase);
        Assert.Equal(3.50m, b.VatAmount);
        Assert.Equal(24.10m, b.Total);
        Assert.Equal([(200m, 0.09m, 18.00m), (50m, 0.07m, 3.50m)], b.Tiers.Select(t => (t.Kwh, t.PricePerKwh, t.Amount)));

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecommendationDbContext>();
        Assert.Equal(1, await db.PlanRecommendations.CountAsync(r => r.Id == body.Id));
        var outbox = await db.OutboxMessages.AsNoTracking().SingleAsync(m => m.AggregateId == body.Id.ToString());
        Assert.Equal("recommendation", outbox.AggregateType);
        Assert.Equal("RecommendationCalculated", outbox.EventType);
        Assert.Equal(1, outbox.EventVersion);
        Assert.Equal(correlationId, outbox.CorrelationId);
        Assert.Equal(24.10m, JsonDocument.Parse(outbox.Payload).RootElement.GetProperty("recommendedTotal").GetDecimal());
    }

    [Fact]
    public async Task Plan_whose_valid_version_is_retired_is_not_offered()
    {
        await SeedStandardAndPremium();
        var retired = Guid.NewGuid();
        await InsertPlan(retired, 1, "Old", "Active", "2026-01-01", 0m, """[{"threshold":null,"pricePerKwh":0.01}]""");
        await InsertPlan(retired, 2, "Old", "Retired", "2026-10-01", 0m, "[]");

        var body = (await (await Post(Household(250m))).Content.ReadFromJsonAsync<RecommendationResponse>())!;

        Assert.Equal("Premium,Standard", string.Join(",", body.Plans.Select(p => p.Name)));
        Assert.DoesNotContain(body.Plans, p => p.PlanId == retired);
    }

    [Fact]
    public async Task Future_version_is_not_used()
    {
        await InsertHousehold();
        var planId = Guid.NewGuid();
        await InsertPlan(planId, 1, "Flat", "Active", "2026-01-01", 0m, """[{"threshold":null,"pricePerKwh":0.10}]""");
        await InsertPlan(planId, 2, "Flat", "Active", "2026-12-01", 0m, """[{"threshold":null,"pricePerKwh":0.50}]""");

        var body = (await (await Post(Household(250m))).Content.ReadFromJsonAsync<RecommendationResponse>())!;

        var quote = Assert.Single(body.Plans);
        Assert.Equal(1, quote.PlanVersionNo);
        // 250 * 0.10 = 25.00; eco tax 1.25; VAT base 26.25; VAT 4.46; total 30.71
        Assert.Equal(30.71m, quote.Total);
    }

    [Fact]
    public async Task Unknown_tax_group_is_rejected()
    {
        await SeedStandardAndPremium();

        var response = await Post(new { consumptionKwh = 250m, taxGroup = "nope" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>();
        Assert.Contains("taxGroup", problem!.Errors.Keys);
    }

    [Fact]
    public async Task Negative_consumption_is_rejected()
    {
        await SeedStandardAndPremium();

        var response = await Post(Household(-1m));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task More_than_3_decimal_places_are_rejected()
    {
        await SeedStandardAndPremium();

        var response = await Post(Household(250.0005m));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>();
        Assert.Equal("At most 3 decimal places.", Assert.Single(problem!.Errors["consumptionKwh"]));
    }

    [Fact]
    public async Task Exactly_3_decimal_places_are_accepted()
    {
        await SeedStandardAndPremium();

        var response = await Post(Household(250.125m));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Get_returns_the_same_body_as_post()
    {
        await SeedStandardAndPremium();
        var created = await Post(Household(250m));
        var postBody = await created.Content.ReadAsStringAsync();

        var get = await factory.CreateClient().GetAsync(created.Headers.Location);

        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.True(JsonElement.DeepEquals(
            JsonDocument.Parse(postBody).RootElement,
            JsonDocument.Parse(await get.Content.ReadAsStringAsync()).RootElement));
    }

    [Fact]
    public async Task Unknown_id_is_not_found()
    {
        var response = await factory.CreateClient().GetAsync($"/recommendations/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
