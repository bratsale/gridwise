namespace GridWise.Batch.Domain;

public class Job
{
    public Guid Id { get; set; }
    public JobStatus Status { get; set; }
    public string FileName { get; set; } = "";
    public int TotalRows { get; set; }
    public int ProcessedRows { get; set; }
    public int FailedRows { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
}
