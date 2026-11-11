using System.Text.Json;
using GridWise.Batch.Events;

namespace GridWise.Batch.Tests;

public class EventContractTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static readonly BatchChunkRequestedV1 Chunk = new(
        Guid.Parse("5eed0000-0000-0000-0000-0000000000b1"), 1,
        [new(2, "c1,250.5,household"), new(4, "c2,80,household")]);

    [Fact]
    public void BatchChunkRequested_v1_serializes_to_golden_file()
    {
        var actual = JsonSerializer.Serialize(Chunk, Web);

        Assert.True(
            JsonElement.DeepEquals(JsonDocument.Parse(Golden()).RootElement, JsonDocument.Parse(actual).RootElement),
            $"Serialized event differs from BatchChunkRequested.v1.json: {actual}");
    }

    // Consumed: the contract is owned by Recommendation.
    [Fact]
    public void BatchChunkCompleted_v1_golden_file_deserializes()
    {
        var e = JsonSerializer.Deserialize<BatchChunkCompletedV1>(Golden("BatchChunkCompleted.v1.json"), Web)!;

        Assert.Equal(Guid.Parse("5eed0000-0000-0000-0000-0000000000b1"), e.JobId);
        Assert.Equal(1, e.ChunkNo);
        Assert.Equal(
            [new RowResultV1(2, "c1", Guid.Parse("5eed0000-0000-0000-0000-000000000001"), 1, "Standard", 24.10m, 1)],
            e.Results);
        Assert.Equal([new RowErrorV1(4, "consumption_kwh 'abc' is not a number.")], e.Errors);
    }

    [Fact]
    public void BatchChunkCompleted_v1_ignores_unknown_fields()
    {
        var withExtra = Golden("BatchChunkCompleted.v1.json").Replace("{", """{ "newOptionalField": 1,""", StringComparison.Ordinal);

        var e = JsonSerializer.Deserialize<BatchChunkCompletedV1>(withExtra, Web)!;

        Assert.Single(e.Results);
        Assert.Single(e.Errors);
    }

    private static string Golden(string file = "BatchChunkRequested.v1.json")
        => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "events", file));
}
