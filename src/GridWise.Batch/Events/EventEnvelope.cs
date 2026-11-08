using System.Text.Json;

namespace GridWise.Batch.Events;

public record EventEnvelope(
    Guid Id,
    string Type,
    int Version,
    DateTimeOffset Timestamp,
    string? CorrelationId,
    JsonElement Data);
