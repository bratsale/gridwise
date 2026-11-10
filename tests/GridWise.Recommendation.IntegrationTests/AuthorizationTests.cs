using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GridWise.Recommendation.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GridWise.Recommendation.IntegrationTests;

[Collection("recommendation")]
public class AuthorizationTests(RecommendationFactory factory) : IAsyncLifetime
{
    private static readonly object Household250 = new { consumptionKwh = 250m, taxGroup = "household" };

    // One active plan and the household tax group, so a recommendation can be calculated.
    public async Task InitializeAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecommendationDbContext>();
        await db.Database.ExecuteSqlRawAsync("DELETE FROM plan_versions");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM tax_group_versions");
        await db.Database.ExecuteSqlInterpolatedAsync($$"""
            INSERT INTO plan_versions (plan_id, version_no, name, discount, status, effective_from, tiers, received_at)
            VALUES ({{Guid.NewGuid()}}, 1, 'Standard', 0.05, 'Active', '2026-01-01', '[{"threshold":null,"pricePerKwh":0.06}]'::jsonb, {{DateTimeOffset.UtcNow}})
            """);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO tax_group_versions (code, version_no, vat, eco_tax, status, effective_from, received_at)
            VALUES ('household', 1, 0.17, 0.005, 'Active', '2026-01-01', {DateTimeOffset.UtcNow})
            """);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private HttpClient Client(string role, string sub) => factory.CreateClient().As(role, sub);

    private async Task<(Guid Id, Guid PlanId)> Calculate(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/recommendations", Household250);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (body.GetProperty("id").GetGuid(), body.GetProperty("recommended").GetProperty("planId").GetGuid());
    }

    [Fact]
    public async Task Without_a_token_it_is_401()
    {
        var response = await factory.CreateClient().Anonymous().PostAsJsonAsync("/recommendations", Household250);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("Bearer", Assert.Single(response.Headers.WwwAuthenticate).Scheme);
    }

    [Fact]
    public async Task A_role_other_than_customer_or_admin_is_403()
    {
        var response = await Client("auditor", "auditor-1").PostAsJsonAsync("/recommendations", Household250);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(403, problem.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task The_recommendation_stores_who_created_it()
    {
        var (id, _) = await Calculate(Client(TestAuth.Customer, "customer-owner"));

        using var scope = factory.Services.CreateScope();
        var row = await scope.ServiceProvider.GetRequiredService<RecommendationDbContext>()
            .PlanRecommendations.AsNoTracking().SingleAsync(r => r.Id == id);
        Assert.Equal("customer-owner", row.CreatedBy);
    }

    [Fact]
    public async Task A_customer_reads_and_accepts_only_their_own_another_customer_gets_404()
    {
        var owner = Client(TestAuth.Customer, "customer-a");
        var other = Client(TestAuth.Customer, "customer-b");
        var (id, planId) = await Calculate(owner);

        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/recommendations/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsJsonAsync($"/recommendations/{id}/accept", new { planId })).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync($"/recommendations/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await owner.PostAsJsonAsync($"/recommendations/{id}/accept", new { planId })).StatusCode);
    }

    [Fact]
    public async Task An_admin_reads_any_recommendation_but_accepts_only_their_own()
    {
        var (id, planId) = await Calculate(Client(TestAuth.Customer, "customer-c"));
        var admin = Client(TestAuth.Admin, "admin-1");

        var read = await admin.GetAsync($"/recommendations/{id}");
        var accept = await admin.PostAsJsonAsync($"/recommendations/{id}/accept", new { planId });

        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Equal(id, (await read.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());
        Assert.Equal(HttpStatusCode.NotFound, accept.StatusCode);
    }

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    [InlineData("/openapi/v1.json")]
    public async Task Health_and_openapi_need_no_token(string path)
    {
        var response = await factory.CreateClient().Anonymous().GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
