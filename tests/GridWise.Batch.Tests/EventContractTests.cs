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

    [Fact]
    public void BatchChunkRequested_v1_golden_file_deserializes()
    {
        var e = JsonSerializer.Deserialize<BatchChunkRequestedV1>(Golden(), Web)!;

        Assert.Equal(Chunk.JobId, e.JobId);
        Assert.Equal(Chunk.ChunkNo, e.ChunkNo);
        Assert.Equal(Chunk.Rows, e.Rows);
    }

    private static string Golden()
        => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "events", "BatchChunkRequested.v1.json"));
}
