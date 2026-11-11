using System.Text.Json;
using GridWise.Catalog.Events;

namespace GridWise.Catalog.Tests;

public class EventContractTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static readonly PlanVersionCreatedV1 Plan = new(
        Guid.Parse("5eed0000-0000-0000-0000-000000000001"), 1, "Standard", 0.05m, "Active",
        new DateOnly(2026, 1, 1),
        [new(100m, 0.10m), new(300m, 0.08m), new(500m, 0.07m), new(null, 0.06m)],
        ChangedBy: "6f1c2a9e-0b7d-4c3e-9a51-3d2f8e7b4c10");

    private static readonly TaxGroupVersionCreatedV1 TaxGroup = new(
        "household", 1, 0.17m, 0.005m, "Active", new DateOnly(2026, 1, 1),
        ChangedBy: "6f1c2a9e-0b7d-4c3e-9a51-3d2f8e7b4c10");

    [Fact]
    public void PlanVersionCreated_v1_serializes_to_golden_file()
        => AssertSameJson("PlanVersionCreated.v1.json", JsonSerializer.Serialize(Plan, Web));

    [Fact]
    public void TaxGroupVersionCreated_v1_serializes_to_golden_file()
        => AssertSameJson("TaxGroupVersionCreated.v1.json", JsonSerializer.Serialize(TaxGroup, Web));

    [Fact]
    public void EventEnvelope_v1_serializes_to_golden_file()
    {
        var envelope = new EventEnvelope(
            Guid.Parse("5eed0000-0000-0000-0001-000000000001"), EventTypes.PlanVersionCreated, 1,
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), "seed",
            JsonSerializer.SerializeToElement(Plan with { ChangedBy = null }, Web)); // a seed event: nobody made it

        AssertSameJson("envelope.v1.json", JsonSerializer.Serialize(envelope, Web));
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
