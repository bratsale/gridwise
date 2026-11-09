using System.Text.Json;
using GridWise.Batch.Data;
using GridWise.Batch.Domain;
using GridWise.Batch.Events;
using GridWise.Batch.Telemetry;
using Microsoft.EntityFrameworkCore;

namespace GridWise.Batch.Services;

/// <summary>
/// Splits Pending jobs into chunks and hands the chunks of Running jobs to the workers through the outbox,
/// keeping at most <c>Batch:WindowSize</c> chunks per job in flight.
/// Assumes a single Batch instance: two dispatchers could both split the same job or dispatch the same chunk.
/// </summary>
public class BatchDispatcher(
    IServiceScopeFactory scopeFactory, IConfiguration config, TimeProvider time, ILogger<BatchDispatcher> logger)
    : BackgroundService
{
    public const int DefaultChunkSize = 500;
    public const int DefaultWindowSize = 20;

    private static readonly TimeSpan IdleWait = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ErrorWait = TimeSpan.FromSeconds(5);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var chunkSize = config.GetValue("Batch:ChunkSize", DefaultChunkSize);
        var windowSize = config.GetValue("Batch:WindowSize", DefaultWindowSize);
        if (chunkSize < 1 || windowSize < 1)
            throw new InvalidOperationException("Batch:ChunkSize and Batch:WindowSize must be at least 1.");

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var wait = IdleWait;
                try
                {
                    using var scope = scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<BatchDbContext>();

                    if (await StepAsync(db, time, chunkSize, windowSize, stoppingToken) > 0)
                        wait = TimeSpan.Zero;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex, "Batch dispatching failed.");
                    wait = ErrorWait;
                }

                if (wait > TimeSpan.Zero)
                    await Task.Delay(wait, time, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    /// <returns>How much was done (jobs split plus chunks dispatched); 0 means there was nothing to do.</returns>
    public static async Task<int> StepAsync(
        BatchDbContext db, TimeProvider time, int chunkSize, int windowSize, CancellationToken ct)
    {
        var work = 0;

        var pendingJobIds = await db.Jobs
            .Where(j => j.Status == JobStatus.Pending)
            .OrderBy(j => j.CreatedAt).ThenBy(j => j.Id)
            .Select(j => j.Id)
            .ToListAsync(ct);
        foreach (var jobId in pendingJobIds)
            work += await SplitIntoChunksAsync(db, jobId, time, chunkSize, ct);

        var runningJobIds = await db.Jobs
            .Where(j => j.Status == JobStatus.Running)
            .OrderBy(j => j.CreatedAt).ThenBy(j => j.Id)
            .Select(j => j.Id)
            .ToListAsync(ct);
        foreach (var jobId in runningJobIds)
            work += await DispatchJobAsync(db, jobId, time, windowSize, ct);

        return work;
    }

    // One transaction: all chunk rows and the job turning Running. The job row is locked and its status checked again,
    // so a cancel and the split cannot interleave.
    private static async Task<int> SplitIntoChunksAsync(
        BatchDbContext db, Guid jobId, TimeProvider time, int chunkSize, CancellationToken ct)
    {
        var content = await ReadFileAsync(db, jobId, ct);

        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(ct);

            if (await JobLock.LockAsync(db, jobId, ct) != JobStatus.Pending)
                return 0;

            var chunks = new List<Chunk>();
            Chunk? current = null;
            foreach (var (line, _) in CsvRows.Read(content))
            {
                if (current is null || current.RowCount == chunkSize)
                {
                    current = new Chunk
                    {
                        JobId = jobId,
                        ChunkNo = chunks.Count + 1,
                        FirstLine = line,
                        Status = ChunkStatus.Pending
                    };
                    chunks.Add(current);
                }
                current.RowCount++;
            }

            db.Chunks.AddRange(chunks);
            await db.SaveChangesAsync(ct);
            var now = time.GetUtcNow();
            await db.Jobs.Where(j => j.Id == jobId)
                .ExecuteUpdateAsync(s => s.SetProperty(j => j.Status, JobStatus.Running).SetProperty(j => j.StartedAt, now), ct);
            await transaction.CommitAsync(ct);
            return 1;
        });
    }

    /// <summary>Dispatches the next Pending chunks of a Running job while fewer than <paramref name="windowSize"/> are in flight.</summary>
    public static async Task<int> DispatchJobAsync(
        BatchDbContext db, Guid jobId, TimeProvider time, int windowSize, CancellationToken ct)
    {
        var inFlight = await db.Chunks.CountAsync(c => c.JobId == jobId && c.Status == ChunkStatus.Dispatched, ct);
        var free = windowSize - inFlight;
        if (free <= 0)
            return 0;

        var next = await db.Chunks.AsNoTracking()
            .Where(c => c.JobId == jobId && c.Status == ChunkStatus.Pending)
            .OrderBy(c => c.ChunkNo)
            .Take(free)
            .ToListAsync(ct);
        if (next.Count == 0)
            return 0;

        // The file is read once and walked once for all chunks of this step (they are in file order).
        var content = await ReadFileAsync(db, jobId, ct);
        using var rows = CsvRows.Read(content).GetEnumerator();
        var hasRow = rows.MoveNext();

        var dispatched = 0;
        foreach (var chunk in next)
        {
            while (hasRow && rows.Current.Line < chunk.FirstLine)
                hasRow = rows.MoveNext();

            var chunkRows = new List<ChunkRowV1>(chunk.RowCount);
            while (hasRow && chunkRows.Count < chunk.RowCount)
            {
                chunkRows.Add(new ChunkRowV1(rows.Current.Line, rows.Current.Text));
                hasRow = rows.MoveNext();
            }
            var payload = JsonSerializer.Serialize(new BatchChunkRequestedV1(jobId, chunk.ChunkNo, chunkRows), Json);

            // One transaction per chunk: Dispatched and its outbox row. The job row is locked and its status checked again,
            // so a cancel and a dispatch cannot interleave: after the cancel commits, nothing more is dispatched.
            // The start of the chunk's trace: the outbox row stores this span, so the publish, the worker and the result are its children.
            using var dispatch = Tracing.Source.StartActivity("dispatch chunk");
            dispatch?.SetTag("job.id", jobId.ToString());
            dispatch?.SetTag("chunk.no", chunk.ChunkNo);

            var done = await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                db.ChangeTracker.Clear();
                await using var transaction = await db.Database.BeginTransactionAsync(ct);

                if (await JobLock.LockAsync(db, jobId, ct) != JobStatus.Running)
                    return false;

                var now = time.GetUtcNow();
                var updated = await db.Chunks
                    .Where(c => c.JobId == jobId && c.ChunkNo == chunk.ChunkNo && c.Status == ChunkStatus.Pending)
                    .ExecuteUpdateAsync(s => s.SetProperty(c => c.Status, ChunkStatus.Dispatched).SetProperty(c => c.DispatchedAt, now), ct);
                if (updated == 0)
                    return true; // not Pending any more: nothing to send for this chunk

                db.OutboxMessages.Add(new OutboxMessage
                {
                    Id = Guid.CreateVersion7(),
                    AggregateType = "batch-chunk",
                    AggregateId = $"{jobId}:{chunk.ChunkNo}", // the Kafka key: spreads the chunks of a job over all partitions
                    EventType = EventTypes.BatchChunkRequested,
                    EventVersion = 1,
                    Payload = payload,
                    TraceParent = Tracing.CurrentTraceParent(),
                    OccurredAt = now
                });
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                dispatched++;
                return true;
            });

            if (!done)
                break; // the job is not Running any more
        }

        return dispatched;
    }

    private static Task<byte[]> ReadFileAsync(BatchDbContext db, Guid jobId, CancellationToken ct)
        => db.JobFiles.AsNoTracking().Where(f => f.JobId == jobId).Select(f => f.Content).SingleAsync(ct);
}
