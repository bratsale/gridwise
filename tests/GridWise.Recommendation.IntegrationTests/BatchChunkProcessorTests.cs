using System.Net.Http.Json;
using System.Text.Json;
using GridWise.Recommendation.Api;
using GridWise.Recommendation.Data;
using GridWise.Recommendation.Events;
using GridWise.Recommendation.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GridWise.Recommendation.IntegrationTests;

[Collection("recommendation")]
public class BatchChunkProcessorTests(RecommendationFactory factory) : IAsyncLifetime
{
    private const string StandardTiers = """[{"threshold":100,"pricePerKwh":0.10},{"threshold":300,"pricePerKwh":0.08},{"threshold":500,"pricePerKwh":0.07},{"threshold":null,"pricePerKwh":0.06}]""";
    private const string PremiumTiers = """[{"threshold":200,"pricePerKwh":0.09},{"threshold":400,"pricePerKwh":0.07},{"threshold":600,"pricePerKwh":0.05},{"threshold":null,"pricePerKwh":0.04}]""";

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    // Other tests in this collection leave copy rows behind, and a chunk prices every plan.
    public async Task InitializeAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecommendationDbContext>();
        await db.Database.ExecuteSqlRawAsync("DELETE FROM plan_versions");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM tax_group_versions");
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO plan_versions (plan_id, version_no, name, discount, status, effective_from, tiers, received_at) VALUES
              ({Guid.NewGuid()}, 1, 'Standard', 0.05, 'Active', '2026-01-01', {StandardTiers}::jsonb, now()),
              ({Guid.NewGuid()}, 1, 'Premium', 0.10, 'Active', '2026-01-01', {PremiumTiers}::jsonb, now())
            """);
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO tax_group_versions (code, version_no, vat, eco_tax, status, effective_from, received_at) VALUES
              ('household', 1, 0.17, 0.005, 'Active', '2026-01-01', now()),
              ('old', 1, 0.17, 0.005, 'Active', '2026-01-01', now()),
              ('old', 2, 0.17, 0.005, 'Retired', '2026-06-01', now())
            """);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // Lines are numbered from 2, like the first data row of a file with a header.
    internal static string Chunk(Guid jobId, int chunkNo, params string[] rows)
        => JsonSerializer.Serialize(new
        {
            id = Guid.NewGuid(),
            type = "BatchChunkRequested",
            version = 1,
            timestamp = DateTimeOffset.UtcNow,
            correlationId = (string?)null,
            data = new { jobId, chunkNo, rows = rows.Select((text, i) => new { line = i + 2, text }) }
        }, Web);

    private async Task<ChunkOutcome> Handle(string json)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<BatchChunkProcessor>().HandleAsync(json, CancellationToken.None);
    }

    private async Task<(int Processed, List<Domain.OutboxMessage> Outbox)> Written(Guid jobId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RecommendationDbContext>();
        return (
            await db.ProcessedChunks.CountAsync(c => c.JobId == jobId),
            await db.OutboxMessages.AsNoTracking().Where(m => m.AggregateId == jobId.ToString()).ToListAsync());
    }

    private static BatchChunkCompletedV1 Completed(Domain.OutboxMessage row)
        => JsonSerializer.Deserialize<BatchChunkCompletedV1>(row.Payload, Web)!;

    [Fact]
    public async Task Valid_chunk_gives_one_outbox_row_with_the_results()
    {
        var jobId = Guid.NewGuid();

        var outcome = await Handle(Chunk(jobId, 7, "c1,250,household", " c2 , 80.5 , household "));

        Assert.Equal(ChunkOutcome.Processed, outcome);
        var (processed, outbox) = await Written(jobId);
        Assert.Equal(1, processed);
        var row = Assert.Single(outbox);
        Assert.Equal("batch-chunk-result", row.AggregateType);
        Assert.Equal(EventTypes.BatchChunkCompleted, row.EventType);
        Assert.Equal(1, row.EventVersion);
        Assert.Null(row.PublishedAt);

        var e = Completed(row);
        Assert.Equal(jobId, e.JobId);
        Assert.Equal(7, e.ChunkNo);
        Assert.Empty(e.Errors);
        Assert.Equal([(2, "c1"), (3, "c2")], e.Results.Select(r => (r.Line, r.CustomerId)));
        Assert.All(e.Results, r =>
        {
            Assert.Equal("Premium", r.PlanName);
            Assert.Equal(1, r.PlanVersionNo);
            Assert.Equal(1, r.TaxGroupVersionNo);
        });
        Assert.Equal(24.10m, e.Results[0].Total);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(250)]
    [InlineData(1000.5)]
    public async Task Totals_equal_the_recommendation_api_for_the_same_input(double kwh)
    {
        var consumption = (decimal)kwh;
        var response = await factory.CreateClient().PostAsJsonAsync(
            "/recommendations", new { consumptionKwh = consumption, taxGroup = "household" });
        var expected = (await response.Content.ReadFromJsonAsync<RecommendationResponse>())!.Recommended!;
        var jobId = Guid.NewGuid();

        await Handle(Chunk(jobId, 1, FormattableString.Invariant($"c1,{consumption},household")));

        var result = Assert.Single(Completed(Assert.Single((await Written(jobId)).Outbox)).Results);
        Assert.Equal(expected.Total, result.Total);
        Assert.Equal(expected.PlanId, result.PlanId);
        Assert.Equal(expected.PlanVersionNo, result.PlanVersionNo);
        Assert.Equal(expected.Name, result.PlanName);
    }

    [Theory]
    [InlineData("c1,250", "Expected 3 comma-separated fields, found 2")]
    [InlineData("c1,250,household,extra", "Expected 3 comma-separated fields, found 4")]
    [InlineData(" ,250,household", "customer_id is empty")]
    [InlineData("c1,abc,household", "'abc' is not a number")]
    [InlineData("c1,1e3,household", "'1e3' is not a number")]
    [InlineData("c1,-5,household", "must not be negative")]
    [InlineData("c1,1.2345,household", "more than 3 decimal places")]
    [InlineData("c1,250,nowhere", "Unknown tax group 'nowhere'")]
    [InlineData("c1,250,old", "Tax group 'old' is retired")]
    public async Task Invalid_row_is_reported_with_its_line_and_the_rest_of_the_chunk_is_processed(string bad, string expectedError)
    {
        var jobId = Guid.NewGuid();

        var outcome = await Handle(Chunk(jobId, 1, "c1,100,household", bad, "c3,300,household"));

        Assert.Equal(ChunkOutcome.Processed, outcome);
        var e = Completed(Assert.Single((await Written(jobId)).Outbox));
        var error = Assert.Single(e.Errors);
        Assert.Equal(3, error.Line);
        Assert.Contains(expectedError, error.Error);
        Assert.Equal([2, 4], e.Results.Select(r => r.Line));
    }

    [Fact]
    public async Task Customer_id_of_101_characters_is_rejected_and_100_is_accepted()
    {
        var jobId = Guid.NewGuid();

        await Handle(Chunk(jobId, 1, $"{new string('x', 100)},100,household", $"{new string('x', 101)},100,household"));

        var e = Completed(Assert.Single((await Written(jobId)).Outbox));
        Assert.Equal([2], e.Results.Select(r => r.Line));
        Assert.Equal(3, Assert.Single(e.Errors).Line);
        Assert.Contains("longer than 100", e.Errors[0].Error);
    }

    [Fact]
    public async Task Same_chunk_twice_gives_one_processed_row_and_one_outbox_row()
    {
        var jobId = Guid.NewGuid();
        var json = Chunk(jobId, 1, "c1,250,household");

        Assert.Equal(ChunkOutcome.Processed, await Handle(json));
        Assert.Equal(ChunkOutcome.Duplicate, await Handle(json));

        var (processed, outbox) = await Written(jobId);
        Assert.Equal(1, processed);
        Assert.Single(outbox);
    }

    [Fact]
    public async Task Other_event_types_are_skipped_and_unreadable_messages_are_poison()
    {
        var other = Chunk(Guid.NewGuid(), 1, "c1,1,household").Replace("BatchChunkRequested", "SomethingElse");
        Assert.Equal(ChunkOutcome.SkippedUnknownType, await Handle(other));

        await Assert.ThrowsAsync<PoisonMessageException>(() => Handle("not json"));
        await Assert.ThrowsAsync<PoisonMessageException>(
            () => Handle(Chunk(Guid.NewGuid(), 1).Replace("\"version\":1", "\"version\":2")));
    }
}
