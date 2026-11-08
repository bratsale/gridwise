using GridWise.Batch.Data;
using GridWise.Batch.Domain;
using GridWise.Batch.Events;
using Microsoft.EntityFrameworkCore;

namespace GridWise.Batch.Services;

public enum ResultOutcome
{
    Applied,
    /// <summary>The chunk was already completed: a repeated message.</summary>
    Duplicate,
    /// <summary>No such job, or no such chunk, or the chunk was never dispatched.</summary>
    UnknownChunk,
    JobCancelled,
    SkippedUnknownType
}

/// <summary>Stores the result of one chunk and completes the job when it was the last one, all in one transaction.</summary>
public class BatchResultHandler(BatchDbContext db, TimeProvider time)
{
    // Virtual so that tests can simulate failures.
    public virtual async Task<ResultOutcome> HandleAsync(string json, CancellationToken ct)
    {
        var envelope = EventReader.Parse(json);
        if (envelope.Type != EventTypes.BatchChunkCompleted)
            return ResultOutcome.SkippedUnknownType;

        var e = EventReader.ReadData<BatchChunkCompletedV1>(envelope);
        if (e.Results is null || e.Errors is null)
            throw new PoisonMessageException($"{envelope.Type} has no results or errors list.");

        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(ct);

            // The row lock serializes this with a cancel request and with any other result of the same job.
            var job = (await db.Jobs
                .FromSqlInterpolated($"SELECT * FROM jobs WHERE id = {e.JobId} FOR UPDATE")
                .ToListAsync(ct)).SingleOrDefault();
            if (job is null)
                return ResultOutcome.UnknownChunk;
            if (job.Status == JobStatus.Cancelled)
                return ResultOutcome.JobCancelled; // write nothing: the transaction is rolled back

            var now = time.GetUtcNow();
            var updated = await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE chunks SET status = 'Completed', completed_at = {now}
                WHERE job_id = {e.JobId} AND chunk_no = {e.ChunkNo} AND status = 'Dispatched'
                """, ct);
            if (updated == 0)
            {
                var status = await db.Chunks
                    .Where(c => c.JobId == e.JobId && c.ChunkNo == e.ChunkNo)
                    .Select(c => (ChunkStatus?)c.Status)
                    .SingleOrDefaultAsync(ct);
                return status == ChunkStatus.Completed ? ResultOutcome.Duplicate : ResultOutcome.UnknownChunk;
            }

            db.JobResults.AddRange(e.Results.Select(r => new JobResult
            {
                JobId = e.JobId, Line = r.Line, CustomerId = r.CustomerId, PlanId = r.PlanId, PlanVersionNo = r.PlanVersionNo,
                PlanName = r.PlanName, Total = r.Total, TaxGroupVersionNo = r.TaxGroupVersionNo
            }));
            db.JobErrors.AddRange(e.Errors.Select(r => new JobError { JobId = e.JobId, Line = r.Line, Error = r.Error }));
            job.ProcessedRows += e.Results.Count;
            job.FailedRows += e.Errors.Count;

            // The UPDATE above is part of this transaction, so this already counts the chunk just completed.
            if (!await db.Chunks.AnyAsync(c => c.JobId == e.JobId && c.Status != ChunkStatus.Completed, ct))
            {
                job.Status = JobStatus.Completed;
                job.FinishedAt = now;
            }

            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return ResultOutcome.Applied;
        });
    }
}
