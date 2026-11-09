namespace GridWise.Recommendation.Domain;

public class OutboxMessage
{
    public Guid Id { get; set; }
    public string AggregateType { get; set; } = null!;
    public string AggregateId { get; set; } = null!;
    public string EventType { get; set; } = null!;
    public int EventVersion { get; set; }
    public string Payload { get; set; } = null!;
    /// <summary>W3C traceparent of the code that wrote the row; the publisher continues that trace.</summary>
    public string? TraceParent { get; set; }
    public string? CorrelationId { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
}
