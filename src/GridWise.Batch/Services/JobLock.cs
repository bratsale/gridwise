using GridWise.Batch.Data;
using GridWise.Batch.Domain;
using Microsoft.EntityFrameworkCore;

namespace GridWise.Batch.Services;

public static class JobLock
{
    /// <summary>
    /// Locks the job row until the current transaction ends and returns its status (null if there is no such job).
    /// Cancel, the dispatcher and the result handler all take this lock, so they cannot interleave on one job.
    /// Must be called inside a transaction.
    /// </summary>
    public static async Task<JobStatus?> LockAsync(BatchDbContext db, Guid jobId, CancellationToken ct)
    {
        var status = await db.Database
            .SqlQuery<string>($"SELECT status AS \"Value\" FROM jobs WHERE id = {jobId} FOR UPDATE")
            .ToListAsync(ct);

        return status.Count == 0 ? null : Enum.Parse<JobStatus>(status[0]);
    }
}
