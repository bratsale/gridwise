namespace GridWise.Batch.Events;

/// <summary>One CSV data row: its line number in the original file and the raw text of the line.</summary>
public record ChunkRowV1(int Line, string Text);

public record BatchChunkRequestedV1(Guid JobId, int ChunkNo, IReadOnlyList<ChunkRowV1> Rows);

// Consumed from the workers (batch.results). Recommendation owns the contract: docs/events/BatchChunkCompleted.v1.json.
public record RowResultV1(int Line, string CustomerId, Guid PlanId, int PlanVersionNo, string PlanName, decimal Total, int TaxGroupVersionNo);

public record RowErrorV1(int Line, string Error);

public record BatchChunkCompletedV1(Guid JobId, int ChunkNo, IReadOnlyList<RowResultV1> Results, IReadOnlyList<RowErrorV1> Errors);
