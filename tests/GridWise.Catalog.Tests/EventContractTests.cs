using System.Text.Json;
using GridWise.Catalog.Events;

namespace GridWise.Catalog.Tests;

public class EventContractTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static readonly PlanVersionCreatedV1 Plan = new(
        Guid.Parse("5eed0000-0000-0000-0000-000000000001"), 1, "Standard", 0.05m, "Active",
        new DateOnly(2026, 1, 1),
        [new(100m, 0.10m), new(300m, 0.08m), new(500m, 0.07m), new(null, 0.06m)]);

    private static readonly TaxGroupVersionCreatedV1 TaxGroup = new(
        "household", 1, 0.17m, 0.005m, "Active", new DateOnly(2026, 1, 1));

    [Fact]
    public void PlanVersionCreated_v1_serializes_to_golden_file()
        => AssertSameJson("PlanVersionCreated.v1.json", JsonSerializer.Serialize(Plan, Web));

    [Fact]
    public void TaxGroupVersionCreated_v1_serializes_to_golden_file()
        => AssertSameJson("TaxGroupVersionCreated.v1.json", JsonSerializer.Serialize(TaxGroup, Web));

    [Fact]
    public void PlanVersionCreated_v1_golden_file_deserializes()
    {
        var e = JsonSerializer.Deserialize<PlanVersionCreatedV1>(Golden("PlanVersionCreated.v1.json"), Web)!;

        Assert.Equal(Plan.Id, e.Id);
        Assert.Equal(Plan.VersionNo, e.VersionNo);
        Assert.Equal(Plan.Name, e.Name);
        Assert.Equal(Plan.Discount, e.Discount);
        Assert.Equal(Plan.Status, e.Status);
        Assert.Equal(Plan.EffectiveFrom, e.EffectiveFrom);
        Assert.Equal(Plan.Tiers.Count, e.Tiers.Count);
        for (var i = 0; i < Plan.Tiers.Count; i++)
        {
            Assert.Equal(Plan.Tiers[i].Threshold, e.Tiers[i].Threshold);
            Assert.Equal(Plan.Tiers[i].PricePerKwh, e.Tiers[i].PricePerKwh);
        }
    }

    [Fact]
    public void TaxGroupVersionCreated_v1_golden_file_deserializes()
    {
        var e = JsonSerializer.Deserialize<TaxGroupVersionCreatedV1>(Golden("TaxGroupVersionCreated.v1.json"), Web)!;

        Assert.Equal(TaxGroup.Code, e.Code);
        Assert.Equal(TaxGroup.VersionNo, e.VersionNo);
        Assert.Equal(TaxGroup.Vat, e.Vat);
        Assert.Equal(TaxGroup.EcoTax, e.EcoTax);
        Assert.Equal(TaxGroup.Status, e.Status);
        Assert.Equal(TaxGroup.EffectiveFrom, e.EffectiveFrom);
    }

    private static string Golden(string file)
        => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "events", file));

    private static void AssertSameJson(string file, string actual)
        => Assert.True(
            JsonElement.DeepEquals(
                JsonDocument.Parse(Golden(file)).RootElement,
                JsonDocument.Parse(actual).RootElement),
            $"Serialized event differs from {file}: {actual}");
}
