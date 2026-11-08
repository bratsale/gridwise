using GridWise.Batch.Domain;

namespace GridWise.Batch.Api;

public record JobCreatedResponse(Guid Id, string Status, int TotalRows);

public record JobResponse(
    Guid Id,
    string Status,
    string FileName,
    int TotalRows,
    int TotalChunks,
    int CompletedChunks,
    int ProcessedRows,
    int FailedRows,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt)
{
    public static JobResponse From(Job j, int totalChunks, int completedChunks)
        => new(j.Id, j.Status.ToString(), j.FileName, j.TotalRows, totalChunks, completedChunks,
            j.ProcessedRows, j.FailedRows, j.CreatedAt, j.StartedAt, j.FinishedAt);
}
