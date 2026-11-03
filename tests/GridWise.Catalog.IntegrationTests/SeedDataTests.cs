using System.Net.Http.Json;
using GridWise.Catalog.Api;
using GridWise.Catalog.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GridWise.Catalog.IntegrationTests;

public class SeedDataTests(CatalogFactory factory) : IClassFixture<CatalogFactory>
{
    private async Task<List<T>> GetList<T>(string url)
        => (await factory.CreateClient().GetFromJsonAsync<List<T>>(url))!;

    [Fact]
    public async Task Plans_are_seeded()
    {
        var plans = await GetList<PlanResponse>("/plans");

        var standard = Assert.Single(plans, p => p.Name == "Standard");
        Assert.Equal(0.05m, standard.Discount);
        Assert.Equal(
            [(100m, 0.10m), (300m, 0.08m), (500m, 0.07m), (null, 0.06m)],
            standard.Tiers.Select(t => (t.Threshold, t.PricePerKwh)));

        var premium = Assert.Single(plans, p => p.Name == "Premium");
        Assert.Equal(0.10m, premium.Discount);
        Assert.Equal(
            [(200m, 0.09m), (400m, 0.07m), (600m, 0.05m), (null, 0.04m)],
            premium.Tiers.Select(t => (t.Threshold, t.PricePerKwh)));
    }

    [Fact]
    public async Task Tax_groups_are_seeded()
    {
        var groups = await GetList<TaxGroupResponse>("/tax-groups");

        var household = Assert.Single(groups, g => g.Code == "household");
        Assert.Equal((0.17m, 0.005m), (household.Vat, household.EcoTax));

        var business = Assert.Single(groups, g => g.Code == "business");
        Assert.Equal((0.20m, 0.010m), (business.Vat, business.EcoTax));
    }

    [Fact]
    public async Task Seeded_outbox_payloads_match_the_api_responses()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var seeded = await db.OutboxMessages.Where(m => m.CorrelationId == "seed").ToListAsync();
        Assert.Equal(4, seeded.Count);

        var client = factory.CreateClient();

        foreach (var message in seeded.Where(m => m.AggregateType == "plan"))
        {
            var payload = System.Text.Json.JsonSerializer.Deserialize<PlanResponse>(message.Payload, JsonOptions)!;
            var current = (await client.GetFromJsonAsync<PlanResponse>($"/plans/{message.AggregateId}/versions/1"))!;

            Assert.Equal(current, payload with { Tiers = current.Tiers });
            Assert.Equal(current.Tiers, payload.Tiers);
        }

        foreach (var message in seeded.Where(m => m.AggregateType == "tax-group"))
        {
            var payload = System.Text.Json.JsonSerializer.Deserialize<TaxGroupResponse>(message.Payload, JsonOptions)!;
            var current = await client.GetFromJsonAsync<TaxGroupResponse>($"/tax-groups/{message.AggregateId}/versions/1");

            Assert.Equal(current, payload);
        }
    }

    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new(System.Text.Json.JsonSerializerDefaults.Web);
}
