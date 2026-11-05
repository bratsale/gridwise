using System.Text.Json;
using GridWise.Recommendation.Data;
using GridWise.Recommendation.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GridWise.Recommendation.IntegrationTests;

[Collection("recommendation")]
public class CatalogEventHandlerTests(RecommendationFactory factory)
{
    internal static string PlanEnvelope(Guid planId, int versionNo = 1, int envelopeVersion = 1, string type = "PlanVersionCreated")
        => JsonSerializer.Serialize(new
        {
            id = Guid.NewGuid(),
            type,
            version = envelopeVersion,
            timestamp = DateTimeOffset.UtcNow,
            correlationId = "test",
            data = new
            {
                id = planId,
                versionNo,
                name = "Standard",
                discount = 0.05m,
                status = "Active",
                effectiveFrom = "2026-01-01",
                tiers = new object[]
                {
                    new { threshold = 100m, pricePerKwh = 0.10m },
                    new { threshold = (decimal?)null, pricePerKwh = 0.06m }
                }
            }
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    private async Task<HandleOutcome> Handle(string json)
    {
        using var scope = factory.Services.CreateScope();
        return await factory.Services.GetRequiredService<CatalogEventHandler>().HandleAsync(
            scope.ServiceProvider.GetRequiredService<RecommendationDbContext>(), json, CancellationToken.None);
    }

    [Fact]
    public async Task Plan_version_is_applied_then_reported_as_duplicate()
    {
        var planId = Guid.NewGuid();
        var json = PlanEnvelope(planId);

        Assert.Equal(HandleOutcome.Applied, await Handle(json));
        Assert.Equal(HandleOutcome.Duplicate, await Handle(json));

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecommendationDbContext>();
        var row = await db.PlanVersions.AsNoTracking().SingleAsync(v => v.PlanId == planId);
        Assert.Equal("Standard", row.Name);
        Assert.Equal(0.05m, row.Discount);
        Assert.Equal(new DateOnly(2026, 1, 1), row.EffectiveFrom);
        var tiers = JsonDocument.Parse(row.Tiers).RootElement;
        Assert.Equal(2, tiers.GetArrayLength());
        Assert.Equal(100m, tiers[0].GetProperty("threshold").GetDecimal());
        Assert.Equal(JsonValueKind.Null, tiers[1].GetProperty("threshold").ValueKind);
    }

    [Fact]
    public async Task Tax_group_version_is_applied_then_reported_as_duplicate()
    {
        var code = $"tg-{Guid.NewGuid():N}"[..20];
        var json = JsonSerializer.Serialize(new
        {
            id = Guid.NewGuid(),
            type = "TaxGroupVersionCreated",
            version = 1,
            timestamp = DateTimeOffset.UtcNow,
            correlationId = (string?)null,
            data = new { code, versionNo = 1, vat = 0.17m, ecoTax = 0.005m, status = "Active", effectiveFrom = "2026-01-01" }
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Equal(HandleOutcome.Applied, await Handle(json));
        Assert.Equal(HandleOutcome.Duplicate, await Handle(json));

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecommendationDbContext>();
        var row = await db.TaxGroupVersions.AsNoTracking().SingleAsync(v => v.Code == code);
        Assert.Equal(0.17m, row.Vat);
        Assert.Equal(0.005m, row.EcoTax);
    }

    [Fact]
    public async Task Unknown_event_type_is_skipped()
        => Assert.Equal(HandleOutcome.SkippedUnknownType, await Handle(PlanEnvelope(Guid.NewGuid(), type: "PlanRetired")));

    [Fact]
    public async Task Known_type_with_unknown_version_is_poison()
        => await Assert.ThrowsAsync<PoisonMessageException>(() => Handle(PlanEnvelope(Guid.NewGuid(), envelopeVersion: 99)));

    [Fact]
    public async Task Unparseable_json_is_poison()
        => await Assert.ThrowsAsync<PoisonMessageException>(() => Handle("not json"));
}
