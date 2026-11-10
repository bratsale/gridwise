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
    /// <summary>The sub of the admin who uploaded the file.</summary>
    public string? CreatedBy { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
}
