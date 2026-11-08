namespace GridWise.Batch.Domain;

public class JobFile
{
    public Guid JobId { get; set; }
    public byte[] Content { get; set; } = [];
}
