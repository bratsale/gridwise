using System.Text;
using System.Text.Json;
using GridWise.Batch.Data;
using GridWise.Batch.Domain;
using GridWise.Batch.Services;
using Microsoft.EntityFrameworkCore;

namespace GridWise.Batch.Api;

/// <summary>Cancel, live progress (server-sent events) and the result downloads of a job.</summary>
public static class JobActionEndpoints
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapJobActionEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/jobs/{id:guid}/cancel", CancelJob)
            .WithName("CancelJob")
            .WithTags("Jobs")
            .Produces<JobResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        app.MapGet("/jobs/{id:guid}/events", StreamEvents)
            .WithName("JobEvents")
            .WithTags("Jobs")
            .Produces(StatusCodes.Status200OK, contentType: "text/event-stream")
            .ProducesProblem(StatusCodes.Status404NotFound);

        app.MapGet("/jobs/{id:guid}/results.csv", DownloadResults)
            .WithName("DownloadResults")
            .WithTags("Jobs")
            .Produces(StatusCodes.Status200OK, contentType: "text/csv")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        app.MapGet("/jobs/{id:guid}/errors.csv", DownloadErrors)
            .WithName("DownloadErrors")
            .WithTags("Jobs")
            .Produces(StatusCodes.Status200OK, contentType: "text/csv")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }

    // ---- Cancel ----

    private sealed record CancelOutcome(Job? Job, bool Conflict);

    private static async Task<IResult> CancelJob(Guid id, BatchDbContext db, TimeProvider time, CancellationToken ct)
    {
        var outcome = await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(ct);

            // The same row lock as the result handler and the dispatcher take.
            var job = (await db.Jobs
                .FromSqlInterpolated($"SELECT * FROM jobs WHERE id = {id} FOR UPDATE")
                .ToListAsync(ct)).SingleOrDefault();

            switch (job?.Status)
            {
                case null:
                    return new CancelOutcome(null, false);
                case JobStatus.Completed or JobStatus.Failed:
                    return new CancelOutcome(job, true);
                case JobStatus.Cancelled:
                    return new CancelOutcome(job, false); // idempotent
            }

            job.Status = JobStatus.Cancelled;
            job.FinishedAt = time.GetUtcNow();
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return new CancelOutcome(job, false);
        });

        if (outcome.Job is null)
            return NotFound();
        if (outcome.Conflict)
            return Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: $"A {outcome.Job.Status} job cannot be cancelled.");

        var counts = await JobEndpoints.ChunkCountsAsync(db, [id], ct);
        return Results.Ok(JobEndpoints.ToResponse(outcome.Job, counts));
    }

    // ---- Progress over server-sent events ----

    public record ProgressEvent(string Status, int TotalChunks, int CompletedChunks, int ProcessedRows, int FailedRows);

    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan DefaultHeartbeat = TimeSpan.FromSeconds(15);

    // Every event carries the full current state of the job, so nothing has to be replayed: a client that reconnects
    // (EventSource does it by itself) simply gets the current state first and continues from there. The id: field is a counter
    // of this connection and is not used to resume (Last-Event-ID is ignored). The stream is paced with the real clock
    // (not TimeProvider): it only watches the database and has no stored time to fake.
    private static async Task<IResult> StreamEvents(
        Guid id, HttpContext http, BatchDbContext db, IConfiguration config, IHostApplicationLifetime lifetime, CancellationToken requestAborted)
    {
        // An open stream never ends by itself, so it would hold the shutdown until the container is killed (and a killed
        // service leaves its Kafka consumer group late). Closing it when the application stops lets the client reconnect to the new instance.
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(requestAborted, lifetime.ApplicationStopping);
        var ct = linked.Token;

        var pollInterval = config.GetValue<int?>("Batch:SsePollMilliseconds") is { } ms ? TimeSpan.FromMilliseconds(ms) : DefaultPollInterval;
        var heartbeat = config.GetValue<int?>("Batch:SseHeartbeatSeconds") is { } sec ? TimeSpan.FromSeconds(sec) : DefaultHeartbeat;

        var current = await ReadProgressAsync(db, id, ct);
        if (current is null)
            return NotFound();

        http.Response.ContentType = "text/event-stream";
        http.Response.Headers.CacheControl = "no-cache";
        http.Response.Headers["X-Accel-Buffering"] = "no"; // tells nginx not to buffer this response

        try
        {
            var eventId = 0;
            ProgressEvent? last = null;
            var lastWrite = System.Diagnostics.Stopwatch.StartNew();
            while (true)
            {
                if (current is null)
                    break; // the job disappeared

                if (current != last)
                {
                    await WriteAsync(http, $"id: {++eventId}\ndata: {JsonSerializer.Serialize(current, Json)}\n\n", ct);
                    last = current;
                    lastWrite.Restart();

                    if (IsTerminal(current.Status))
                        break; // the final event was sent
                }
                else if (lastWrite.Elapsed >= heartbeat)
                {
                    await WriteAsync(http, ": heartbeat\n\n", ct);
                    lastWrite.Restart();
                }

                await Task.Delay(pollInterval, ct);
                current = await ReadProgressAsync(db, id, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // The client went away, or the service is stopping.
        }

        return Results.Empty;
    }

    private static bool IsTerminal(string status)
        => status is nameof(JobStatus.Completed) or nameof(JobStatus.Cancelled) or nameof(JobStatus.Failed);

    private static async Task WriteAsync(HttpContext http, string text, CancellationToken ct)
    {
        await http.Response.WriteAsync(text, Encoding.UTF8, ct);
        await http.Response.Body.FlushAsync(ct);
    }

    private static async Task<ProgressEvent?> ReadProgressAsync(BatchDbContext db, Guid id, CancellationToken ct)
    {
        var job = await db.Jobs.AsNoTracking().SingleOrDefaultAsync(j => j.Id == id, ct);
        if (job is null)
            return null;

        var counts = await JobEndpoints.ChunkCountsAsync(db, [id], ct);
        var (total, completed) = counts.TryGetValue(id, out var c) ? c : (0, 0);
        return new ProgressEvent(job.Status.ToString(), total, completed, job.ProcessedRows, job.FailedRows);
    }

    // ---- CSV downloads ----

    private static Task<IResult> DownloadResults(Guid id, BatchDbContext db, CancellationToken ct)
        => DownloadAsync(db, id, "results", ct, async (writer, token) =>
        {
            await writer.WriteLineAsync(Row("customer_id", "plan_name", "plan_version", "total"));
            await foreach (var r in db.JobResults.AsNoTracking().Where(r => r.JobId == id).OrderBy(r => r.Line).AsAsyncEnumerable().WithCancellation(token))
                await writer.WriteLineAsync(Row(
                    Safe(r.CustomerId), Safe(r.PlanName), r.PlanVersionNo.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    r.Total.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)));
        });

    private static Task<IResult> DownloadErrors(Guid id, BatchDbContext db, CancellationToken ct)
        => DownloadAsync(db, id, "errors", ct, async (writer, token) =>
        {
            await writer.WriteLineAsync(Row("line", "error"));
            await foreach (var e in db.JobErrors.AsNoTracking().Where(e => e.JobId == id).OrderBy(e => e.Line).AsAsyncEnumerable().WithCancellation(token))
                await writer.WriteLineAsync(Row(e.Line.ToString(System.Globalization.CultureInfo.InvariantCulture), Safe(e.Error)));
        });

    // The rows are read from the database and written to the response one by one: nothing is collected in memory.
    private static async Task<IResult> DownloadAsync(
        BatchDbContext db, Guid id, string kind, CancellationToken ct, Func<StreamWriter, CancellationToken, Task> write)
    {
        var status = await db.Jobs.AsNoTracking().Where(j => j.Id == id).Select(j => (JobStatus?)j.Status).SingleOrDefaultAsync(ct);
        if (status is null)
            return NotFound();
        if (status is not (JobStatus.Completed or JobStatus.Cancelled))
            return Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: $"The {kind} are available when the job is Completed or Cancelled, not while it is {status}.");

        return Results.Stream(async stream =>
        {
            await using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true) { NewLine = "\r\n" };
            await write(writer, ct);
            await writer.FlushAsync(ct);
        }, "text/csv; charset=utf-8", $"{kind}-{id}.csv");
    }

    private static string Row(params string[] fields) => string.Join(',', fields.Select(Escape));

    // CSV injection: a spreadsheet reads a text cell that starts with one of these characters as a formula. Text that comes from
    // outside (customer id from the uploaded file, plan name, error text) gets a single quote in front, which spreadsheets show as plain text.
    // Numbers that the service writes itself (line, plan_version, total) never go through here, so a negative number stays a number.
    private static string Safe(string text)
        => text.Length > 0 && text[0] is '=' or '+' or '-' or '@' or '\t' or '\r' ? "'" + text : text;

    // RFC 4180: a field with a comma, a quote or a line break is quoted, and quotes inside it are doubled.
    private static string Escape(string field)
        => field.AsSpan().IndexOfAny(",\"\r\n") >= 0 ? $"\"{field.Replace("\"", "\"\"")}\"" : field;

    private static IResult NotFound() => Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Job not found.");
}
