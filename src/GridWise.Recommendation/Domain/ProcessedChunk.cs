namespace GridWise.Recommendation.Domain;

public class ProcessedChunk
{
    public Guid JobId { get; set; }
    public int ChunkNo { get; set; }
    public DateTimeOffset ProcessedAt { get; set; }
}
