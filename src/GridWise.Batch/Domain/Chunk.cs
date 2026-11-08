namespace GridWise.Batch.Domain;

public class Chunk
{
    public Guid JobId { get; set; }
    public int ChunkNo { get; set; }
    public int FirstLine { get; set; }
    public int RowCount { get; set; }
    public ChunkStatus Status { get; set; }
    public DateTimeOffset? DispatchedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}
