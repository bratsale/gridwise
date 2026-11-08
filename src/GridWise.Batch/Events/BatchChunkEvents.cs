namespace GridWise.Batch.Events;

/// <summary>One CSV data row: its line number in the original file and the raw text of the line.</summary>
public record ChunkRowV1(int Line, string Text);

public record BatchChunkRequestedV1(Guid JobId, int ChunkNo, IReadOnlyList<ChunkRowV1> Rows);
