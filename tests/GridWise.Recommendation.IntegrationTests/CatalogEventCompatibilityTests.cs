using System.Text.Json;
using GridWise.Recommendation.Data;
using GridWise.Recommendation.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GridWise.Recommendation.IntegrationTests;

// PlanVersionCreated and TaxGroupVersionCreated v1 got an optional changedBy field later. The consumer must apply events from
// before (no changedBy: still in the topics, which are kept forever, and replayed) as well as the current ones.
[Collection("recommendation")]
public class CatalogEventCompatibilityTests(RecommendationFactory factory)
{
    private static string Golden(string file) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "events", file));

    // The golden payload under a fresh key, in an envelope.
    private static string Envelope(string type, string goldenFile, string keyField, string key)
    {
        var data = JsonNodeOf(Golden(goldenFile));
        data[keyField] = key;
        return JsonSerializer.Serialize(new
        {
            id = Guid.NewGuid(), type, version = 1, timestamp = DateTimeOffset.UtcNow, correlationId = "compat", data
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    private static System.Text.Json.Nodes.JsonObject JsonNodeOf(string json) => System.Text.Json.Nodes.JsonNode.Parse(json)!.AsObject();

    private async Task<HandleOutcome> Handle(string json)
    {
        using var scope = factory.Services.CreateScope();
        return await factory.Services.GetRequiredService<CatalogEventHandler>().HandleAsync(
            scope.ServiceProvider.GetRequiredService<RecommendationDbContext>(), json, CancellationToken.None);
    }

    [Theory]
    [InlineData("PlanVersionCreated.v1.without-changedBy.json")]
    [InlineData("PlanVersionCreated.v1.json")]
    public async Task Plan_event_with_or_without_changedBy_is_applied(string goldenFile)
    {
        var planId = Guid.NewGuid();

        Assert.Equal(HandleOutcome.Applied, await Handle(Envelope("PlanVersionCreated", goldenFile, "id", planId.ToString())));

        using var scope = factory.Services.CreateScope();
        var row = await scope.ServiceProvider.GetRequiredService<RecommendationDbContext>()
            .PlanVersions.AsNoTracking().SingleAsync(v => v.PlanId == planId);
        Assert.Equal("Standard", row.Name);
    }

    [Theory]
    [InlineData("TaxGroupVersionCreated.v1.without-changedBy.json")]
    [InlineData("TaxGroupVersionCreated.v1.json")]
    public async Task Tax_group_event_with_or_without_changedBy_is_applied(string goldenFile)
    {
        var code = $"compat-{Guid.NewGuid():N}"[..20];

        Assert.Equal(HandleOutcome.Applied, await Handle(Envelope("TaxGroupVersionCreated", goldenFile, "code", code)));

        using var scope = factory.Services.CreateScope();
        Assert.True(await scope.ServiceProvider.GetRequiredService<RecommendationDbContext>()
            .TaxGroupVersions.AnyAsync(v => v.Code == code));
    }
}
