namespace GridWise.Batch.Domain;

/// <summary>Why one row of the file was rejected.</summary>
public class JobError
{
    public Guid JobId { get; set; }
    public int Line { get; set; }
    public string Error { get; set; } = null!;
}
