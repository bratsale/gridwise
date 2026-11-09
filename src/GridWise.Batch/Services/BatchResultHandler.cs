using GridWise.Batch.Data;
using GridWise.Batch.Domain;
using GridWise.Batch.Events;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

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

/// <summary>Stores the result of one chunk and completes the job when it was the last one, all in one transaction (the rows are written with COPY).</summary>
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

            // Binary COPY on the connection of this transaction: one round trip for all the rows of the chunk instead of
            // an INSERT for each. The primary keys (job_id, line) still reject a line that was stored before.
            var connection = (NpgsqlConnection)db.Database.GetDbConnection();
            await CopyResultsAsync(connection, e, ct);
            await CopyErrorsAsync(connection, e, ct);
            job.ProcessedRows += e.Results.Count;
            job.FailedRows += e.Errors.Count;

            // The UPDATE above is part of this transaction, so this already counts the chunk just completed.
            if (!await db.Chunks.AnyAsync(c => c.JobId == e.JobId && c.Status != ChunkStatus.Completed, ct))
            {
                job.Status = JobStatus.Completed;
                job.FinishedAt = now;
            }

            await db.SaveChangesAsync(ct); // the job row: counters and, for the last chunk, the status
            await transaction.CommitAsync(ct);
            return ResultOutcome.Applied;
        });
    }

    private static async Task CopyResultsAsync(NpgsqlConnection connection, BatchChunkCompletedV1 e, CancellationToken ct)
    {
        if (e.Results.Count == 0)
            return;

        await using var import = await connection.BeginBinaryImportAsync(
            "COPY job_results (job_id, line, customer_id, plan_id, plan_version_no, plan_name, total, tax_group_version_no) FROM STDIN (FORMAT BINARY)", ct);
        foreach (var r in e.Results)
        {
            await import.StartRowAsync(ct);
            await import.WriteAsync(e.JobId, NpgsqlDbType.Uuid, ct);
            await import.WriteAsync(r.Line, NpgsqlDbType.Integer, ct);
            await import.WriteAsync(r.CustomerId, NpgsqlDbType.Varchar, ct);
            await import.WriteAsync(r.PlanId, NpgsqlDbType.Uuid, ct);
            await import.WriteAsync(r.PlanVersionNo, NpgsqlDbType.Integer, ct);
            await import.WriteAsync(r.PlanName, NpgsqlDbType.Varchar, ct);
            await import.WriteAsync(r.Total, NpgsqlDbType.Numeric, ct);
            await import.WriteAsync(r.TaxGroupVersionNo, NpgsqlDbType.Integer, ct);
        }
        await import.CompleteAsync(ct);
    }

    private static async Task CopyErrorsAsync(NpgsqlConnection connection, BatchChunkCompletedV1 e, CancellationToken ct)
    {
        if (e.Errors.Count == 0)
            return;

        await using var import = await connection.BeginBinaryImportAsync("COPY job_errors (job_id, line, error) FROM STDIN (FORMAT BINARY)", ct);
        foreach (var r in e.Errors)
        {
            await import.StartRowAsync(ct);
            await import.WriteAsync(e.JobId, NpgsqlDbType.Uuid, ct);
            await import.WriteAsync(r.Line, NpgsqlDbType.Integer, ct);
            await import.WriteAsync(r.Error, NpgsqlDbType.Varchar, ct);
        }
        await import.CompleteAsync(ct);
    }
}
