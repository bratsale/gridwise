using GridWise.Batch.Domain;

namespace GridWise.Batch.Api;

public record JobCreatedResponse(Guid Id, string Status, int TotalRows);

public record JobResponse(
    Guid Id,
    string Status,
    string FileName,
    int TotalRows,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt)
{
    public static JobResponse From(Job j)
        => new(j.Id, j.Status.ToString(), j.FileName, j.TotalRows, j.CreatedAt, j.StartedAt, j.FinishedAt);
}
