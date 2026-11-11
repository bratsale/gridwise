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

    private async Task<(Guid Standard, Guid Premium)> SeedStandardAndPremium()
    {
        await InsertHousehold();
        var standard = Guid.NewGuid();
        var premium = Guid.NewGuid();
        await InsertPlan(standard, 1, "Standard", "Active", "2026-01-01", 0.05m, StandardTiers);
        await InsertPlan(premium, 1, "Premium", "Active", "2026-01-01", 0.10m, PremiumTiers);
        return (standard, premium);
    }

    private async Task<Guid> CreateRecommendation()
    {
        var response = await Post(Household(250m));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<RecommendationResponse>())!.Id;
    }

    private Task<HttpResponseMessage> Accept(Guid recommendationId, Guid planId, string? correlationId = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/recommendations/{recommendationId}/accept")
        {
            Content = JsonContent.Create(new { planId })
        };
        if (correlationId is not null)
            request.Headers.Add("X-Correlation-Id", correlationId);
        return factory.CreateClient().SendAsync(request);
    }

    private async Task<T> InDb<T>(Func<RecommendationDbContext, Task<T>> query)
    {
        using var scope = factory.Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<RecommendationDbContext>());
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

    // Consumption must be 0 or more, with at most 3 decimal places (kWh as text: attributes cannot hold decimals).
    // A rejected value is reported on consumptionKwh, so the form can show it next to the field.
    [Theory]
    [InlineData("-1", HttpStatusCode.BadRequest, null)]
    [InlineData("250.0005", HttpStatusCode.BadRequest, "At most 3 decimal places.")]
    [InlineData("250.125", HttpStatusCode.Created, null)]
    public async Task Consumption_must_be_0_or_more_with_at_most_3_decimals(string kwh, HttpStatusCode expected, string? message)
    {
        await SeedStandardAndPremium();

        var response = await Post(Household(decimal.Parse(kwh, System.Globalization.CultureInfo.InvariantCulture)));

        Assert.Equal(expected, response.StatusCode);
        if (expected != HttpStatusCode.BadRequest) return;
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>();
        var errors = Assert.Single(problem!.Errors, e => e.Key == "consumptionKwh").Value;
        if (message is not null) Assert.Equal(message, Assert.Single(errors));
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
    public async Task Get_returns_the_original_result_after_a_newer_plan_version_arrives()
    {
        var (_, premium) = await SeedStandardAndPremium();
        var created = await Post(Household(250m));
        var postBody = await created.Content.ReadAsStringAsync();
        await InsertPlan(premium, 2, "Premium", "Active", "2026-11-05", 0.10m, """[{"threshold":null,"pricePerKwh":0.99}]""");

        var get = await factory.CreateClient().GetAsync(created.Headers.Location);

        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.True(JsonElement.DeepEquals(
            JsonDocument.Parse(postBody).RootElement,
            JsonDocument.Parse(await get.Content.ReadAsStringAsync()).RootElement));
    }

    [Fact]
    public async Task Stored_items_hold_only_the_ranking_without_breakdowns()
    {
        await SeedStandardAndPremium();
        var id = await CreateRecommendation();

        var items = (await InDb(db => db.PlanRecommendations.AsNoTracking().SingleAsync(r => r.Id == id))).Items;

        // The ranking of both plans is stored, their breakdowns are not (ADR 0007).
        Assert.Equal(2, JsonDocument.Parse(items).RootElement.GetArrayLength());
        Assert.DoesNotContain("breakdown", items, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Unknown_id_is_not_found()
    {
        var response = await factory.CreateClient().GetAsync($"/recommendations/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Recommended_plan_can_be_accepted()
    {
        var (_, premium) = await SeedStandardAndPremium();
        var id = await CreateRecommendation();
        var correlationId = Guid.NewGuid().ToString();

        var response = await Accept(id, premium, correlationId);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal($"/recommendations/{id}", response.Headers.Location?.ToString());
        var body = (await response.Content.ReadFromJsonAsync<AcceptanceResponse>())!;
        Assert.Equal(id, body.RecommendationId);
        Assert.Equal(premium, body.PlanId);
        Assert.Equal(1, body.PlanVersionNo);
        Assert.Equal(24.10m, body.Total);
        Assert.Equal(new DateTimeOffset(2026, 11, 5, 10, 0, 0, TimeSpan.Zero), body.AcceptedAt);

        var acceptance = await InDb(db => db.Acceptances.AsNoTracking().SingleAsync(a => a.RecommendationId == id));
        Assert.Equal(premium, acceptance.PlanId);
        Assert.Equal(1, acceptance.PlanVersionNo);
        var outbox = await InDb(db => db.OutboxMessages.AsNoTracking()
            .SingleAsync(m => m.AggregateId == id.ToString() && m.EventType == "PlanAccepted"));
        Assert.Equal("recommendation", outbox.AggregateType);
        Assert.Equal(1, outbox.EventVersion);
        Assert.Equal(correlationId, outbox.CorrelationId);
        var payload = JsonDocument.Parse(outbox.Payload).RootElement;
        Assert.Equal(premium, payload.GetProperty("planId").GetGuid());
        Assert.Equal(24.10m, payload.GetProperty("total").GetDecimal());
    }

    [Fact]
    public async Task Accepting_the_same_plan_again_returns_the_same_body()
    {
        var (_, premium) = await SeedStandardAndPremium();
        var id = await CreateRecommendation();

        var first = await Accept(id, premium);
        var second = await Accept(id, premium);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(await first.Content.ReadAsStringAsync(), await second.Content.ReadAsStringAsync());
        Assert.Equal(1, await InDb(db => db.Acceptances.CountAsync(a => a.RecommendationId == id)));
        Assert.Equal(1, await InDb(db => db.OutboxMessages.CountAsync(m => m.AggregateId == id.ToString() && m.EventType == "PlanAccepted")));
    }

    [Fact]
    public async Task Accepting_another_plan_conflicts()
    {
        var (standard, premium) = await SeedStandardAndPremium();
        var id = await CreateRecommendation();
        await Accept(id, premium);

        var response = await Accept(id, standard);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(premium, (await InDb(db => db.Acceptances.AsNoTracking().SingleAsync(a => a.RecommendationId == id))).PlanId);
    }

    [Fact]
    public async Task Plan_outside_the_recommendation_is_rejected()
    {
        await SeedStandardAndPremium();
        var id = await CreateRecommendation();

        var response = await Accept(id, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>();
        Assert.Contains("planId", problem!.Errors.Keys);
        Assert.Equal(0, await InDb(db => db.Acceptances.CountAsync(a => a.RecommendationId == id)));
    }

    [Fact]
    public async Task Accepting_for_unknown_recommendation_is_not_found()
    {
        var response = await Accept(Guid.NewGuid(), Guid.NewGuid());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Newer_plan_version_does_not_change_the_accepted_version()
    {
        var (_, premium) = await SeedStandardAndPremium();
        var id = await CreateRecommendation();
        await InsertPlan(premium, 2, "Premium", "Active", "2026-11-05", 0.10m, """[{"threshold":null,"pricePerKwh":0.99}]""");

        var response = await Accept(id, premium);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = (await response.Content.ReadFromJsonAsync<AcceptanceResponse>())!;
        Assert.Equal(1, body.PlanVersionNo);
        Assert.Equal(24.10m, body.Total);
        Assert.Equal(1, (await InDb(db => db.Acceptances.AsNoTracking().SingleAsync(a => a.RecommendationId == id))).PlanVersionNo);
    }

    [Fact]
    public async Task Parallel_accepts_of_the_same_plan_create_one_acceptance()
    {
        var (_, premium) = await SeedStandardAndPremium();
        var id = await CreateRecommendation();

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => Accept(id, premium)));

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(4, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        var bodies = await Task.WhenAll(responses.Select(r => r.Content.ReadAsStringAsync()));
        Assert.Single(bodies.Distinct());
        Assert.Equal(1, await InDb(db => db.Acceptances.CountAsync(a => a.RecommendationId == id)));
        Assert.Equal(1, await InDb(db => db.OutboxMessages.CountAsync(m => m.AggregateId == id.ToString() && m.EventType == "PlanAccepted")));
    }

    [Fact]
    public async Task Get_shows_the_acceptance()
    {
        var (_, premium) = await SeedStandardAndPremium();
        var id = await CreateRecommendation();
        var before = (await factory.CreateClient().GetFromJsonAsync<RecommendationResponse>($"/recommendations/{id}"))!;
        Assert.Null(before.Acceptance);

        await Accept(id, premium);

        var after = (await factory.CreateClient().GetFromJsonAsync<RecommendationResponse>($"/recommendations/{id}"))!;
        Assert.NotNull(after.Acceptance);
        Assert.Equal(premium, after.Acceptance.PlanId);
        Assert.Equal(1, after.Acceptance.PlanVersionNo);
        Assert.Equal(24.10m, after.Acceptance.Total);
    }
}
