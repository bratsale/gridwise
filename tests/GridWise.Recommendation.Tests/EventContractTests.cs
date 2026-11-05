using System.Text.Json;
using GridWise.Recommendation.Events;

namespace GridWise.Recommendation.Tests;

public class EventContractTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public void PlanVersionCreated_v1_golden_file_deserializes()
        => AssertPlan(JsonSerializer.Deserialize<PlanVersionCreatedV1>(Golden("PlanVersionCreated.v1.json"), Web)!);

    [Fact]
    public void TaxGroupVersionCreated_v1_golden_file_deserializes()
    {
        var e = JsonSerializer.Deserialize<TaxGroupVersionCreatedV1>(Golden("TaxGroupVersionCreated.v1.json"), Web)!;

        Assert.Equal("household", e.Code);
        Assert.Equal(1, e.VersionNo);
        Assert.Equal(0.17m, e.Vat);
        Assert.Equal(0.005m, e.EcoTax);
        Assert.Equal("Active", e.Status);
        Assert.Equal(new DateOnly(2026, 1, 1), e.EffectiveFrom);
    }

    [Fact]
    public void EventEnvelope_v1_golden_file_deserializes()
    {
        var e = JsonSerializer.Deserialize<EventEnvelope>(Golden("envelope.v1.json"), Web)!;

        Assert.Equal(Guid.Parse("5eed0000-0000-0000-0001-000000000001"), e.Id);
        Assert.Equal(EventTypes.PlanVersionCreated, e.Type);
        Assert.Equal(1, e.Version);
        Assert.Equal(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), e.Timestamp);
        Assert.Equal("seed", e.CorrelationId);
        AssertPlan(e.Data.Deserialize<PlanVersionCreatedV1>(Web)!);
    }

    [Fact]
    public void RecommendationCalculated_v1_serializes_to_golden_file()
    {
        var e = new RecommendationCalculatedV1(
            Guid.Parse("01a1ac3b-bbb3-7eb8-b8b9-7edd0f6f2666"), 250m, "household", 1,
            Guid.Parse("5eed0000-0000-0000-0000-000000000002"), 1, 24.10m,
            new DateTimeOffset(2026, 11, 5, 10, 0, 0, TimeSpan.Zero));

        Assert.True(
            JsonElement.DeepEquals(
                JsonDocument.Parse(Golden("RecommendationCalculated.v1.json")).RootElement,
                JsonSerializer.SerializeToElement(e, Web)),
            "Serialized event differs from RecommendationCalculated.v1.json");
    }

    [Fact]
    public void RecommendationCalculated_v1_golden_file_deserializes()
    {
        var e = JsonSerializer.Deserialize<RecommendationCalculatedV1>(Golden("RecommendationCalculated.v1.json"), Web)!;

        Assert.Equal(Guid.Parse("01a1ac3b-bbb3-7eb8-b8b9-7edd0f6f2666"), e.RecommendationId);
        Assert.Equal(250m, e.ConsumptionKwh);
        Assert.Equal("household", e.TaxGroupCode);
        Assert.Equal(1, e.TaxGroupVersionNo);
        Assert.Equal(Guid.Parse("5eed0000-0000-0000-0000-000000000002"), e.RecommendedPlanId);
        Assert.Equal(1, e.RecommendedPlanVersionNo);
        Assert.Equal(24.10m, e.RecommendedTotal);
        Assert.Equal(new DateTimeOffset(2026, 11, 5, 10, 0, 0, TimeSpan.Zero), e.CalculatedAt);
    }

    [Fact]
    public void Unknown_fields_are_ignored()
    {
        var withExtra = Golden("PlanVersionCreated.v1.json").Replace("{", """{ "description": "new optional field",""", StringComparison.Ordinal);

        AssertPlan(JsonSerializer.Deserialize<PlanVersionCreatedV1>(withExtra, Web)!);
    }

    private static void AssertPlan(PlanVersionCreatedV1 e)
    {
        Assert.Equal(Guid.Parse("5eed0000-0000-0000-0000-000000000001"), e.Id);
        Assert.Equal(1, e.VersionNo);
        Assert.Equal("Standard", e.Name);
        Assert.Equal(0.05m, e.Discount);
        Assert.Equal("Active", e.Status);
        Assert.Equal(new DateOnly(2026, 1, 1), e.EffectiveFrom);

        decimal?[] thresholds = [100m, 300m, 500m, null];
        decimal[] prices = [0.10m, 0.08m, 0.07m, 0.06m];
        Assert.Equal(thresholds.Length, e.Tiers.Count);
        for (var i = 0; i < thresholds.Length; i++)
        {
            Assert.Equal(thresholds[i], e.Tiers[i].Threshold);
            Assert.Equal(prices[i], e.Tiers[i].PricePerKwh);
        }
    }

    private static string Golden(string file)
        => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "events", file));
}
