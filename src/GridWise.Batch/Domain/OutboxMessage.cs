namespace GridWise.Batch.Domain;

public class OutboxMessage
{
    public Guid Id { get; set; }
    public string AggregateType { get; set; } = null!;
    public string AggregateId { get; set; } = null!;
    public string EventType { get; set; } = null!;
    public int EventVersion { get; set; }
    public string Payload { get; set; } = null!;
    public string? CorrelationId { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
}
