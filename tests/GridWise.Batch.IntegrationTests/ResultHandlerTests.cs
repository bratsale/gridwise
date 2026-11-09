using System.Net.Http.Json;
using System.Text.Json;
using GridWise.Batch.Domain;
using GridWise.Batch.Services;
using Microsoft.EntityFrameworkCore;

namespace GridWise.Batch.IntegrationTests;

[Collection("batch-results")]
public class ResultHandlerTests(BatchFactory factory)
{
    private readonly ResultTestSupport _t = new(factory);

    [Fact]
    public async Task Results_and_errors_are_stored_with_correct_counts_and_the_job_completes()
    {
        var jobId = await _t.UploadAndDispatch(3);

        var outcome = await _t.Handle(ResultTestSupport.Completed(jobId, 1, results: 2, errors: 1));

        Assert.Equal(ResultOutcome.Applied, outcome);
        var job = await _t.GetJob(jobId);
        Assert.Equal(JobStatus.Completed, job.Status);
        Assert.Equal(factory.Time.GetUtcNow(), job.FinishedAt);
        Assert.Equal(2, job.ProcessedRows);
        Assert.Equal(1, job.FailedRows);
        var chunk = Assert.Single(await _t.GetChunks(jobId));
        Assert.Equal(ChunkStatus.Completed, chunk.Status);
        Assert.Equal(factory.Time.GetUtcNow(), chunk.CompletedAt);

        var (results, errors) = await _t.InDb(async db => (
            await db.JobResults.AsNoTracking().Where(r => r.JobId == jobId).OrderBy(r => r.Line).ToListAsync(),
            await db.JobErrors.AsNoTracking().Where(e => e.JobId == jobId).ToListAsync()));
        Assert.Equal([2, 3], results.Select(r => r.Line));
        Assert.Equal(("c2", "Standard", 24.10m, 1), (results[0].CustomerId, results[0].PlanName, results[0].Total, results[0].PlanVersionNo));
        Assert.Equal(4, Assert.Single(errors).Line);

        var api = await factory.CreateClient().GetFromJsonAsync<JsonElement>($"/jobs/{jobId}");
        Assert.Equal("Completed", api.GetProperty("status").GetString());
        Assert.Equal(1, api.GetProperty("totalChunks").GetInt32());
        Assert.Equal(1, api.GetProperty("completedChunks").GetInt32());
        Assert.Equal(2, api.GetProperty("processedRows").GetInt32());
        Assert.Equal(1, api.GetProperty("failedRows").GetInt32());
    }

    [Fact]
    public async Task The_same_result_twice_changes_nothing()
    {
        var jobId = await _t.UploadAndDispatch(1200); // 3 chunks, so the job stays Running
        var json = ResultTestSupport.Completed(jobId, 1, results: 4, errors: 1);

        Assert.Equal(ResultOutcome.Applied, await _t.Handle(json));
        Assert.Equal(ResultOutcome.Duplicate, await _t.Handle(json));

        var job = await _t.GetJob(jobId);
        Assert.Equal(JobStatus.Running, job.Status);
        Assert.Equal(4, job.ProcessedRows);
        Assert.Equal(1, job.FailedRows);
        Assert.Equal(4, await _t.InDb(db => db.JobResults.CountAsync(r => r.JobId == jobId)));
        Assert.Equal(1, await _t.InDb(db => db.JobErrors.CountAsync(r => r.JobId == jobId)));
    }

    [Fact]
    public async Task The_job_completes_only_after_its_last_chunk()
    {
        var jobId = await _t.UploadAndDispatch(1200); // 500 + 500 + 200

        await _t.Handle(ResultTestSupport.Completed(jobId, 2, 500, 0, firstLine: 502));
        await _t.Handle(ResultTestSupport.Completed(jobId, 1, 499, 1, firstLine: 2));
        var midway = await _t.GetJob(jobId);
        Assert.Equal(JobStatus.Running, midway.Status);
        Assert.Null(midway.FinishedAt);

        var api = await factory.CreateClient().GetFromJsonAsync<JsonElement>($"/jobs/{jobId}");
        Assert.Equal(3, api.GetProperty("totalChunks").GetInt32());
        Assert.Equal(2, api.GetProperty("completedChunks").GetInt32());

        await _t.Handle(ResultTestSupport.Completed(jobId, 3, 200, 0, firstLine: 1002));

        var done = await _t.GetJob(jobId);
        Assert.Equal(JobStatus.Completed, done.Status);
        Assert.NotNull(done.FinishedAt);
        Assert.Equal(1199, done.ProcessedRows);
        Assert.Equal(1, done.FailedRows);
    }

    [Fact]
    public async Task With_21_chunks_the_21st_is_dispatched_after_one_result_arrives()
    {
        var jobId = await _t.UploadAndDispatch(10_001);
        Assert.Equal(20, (await _t.GetChunks(jobId)).Count(c => c.Status == ChunkStatus.Dispatched));
        Assert.Equal(20, await _t.InDb(db => db.OutboxMessages.CountAsync(m => m.AggregateId.StartsWith(jobId + ":"))));

        await _t.Handle(ResultTestSupport.Completed(jobId, 1, 500, 0));
        await _t.Step(); // the dispatcher sees a free slot

        var chunks = await _t.GetChunks(jobId);
        Assert.Equal(ChunkStatus.Completed, chunks[0].Status);
        Assert.Equal(ChunkStatus.Dispatched, chunks[20].Status);
        Assert.Equal(21, await _t.InDb(db => db.OutboxMessages.CountAsync(m => m.AggregateId.StartsWith(jobId + ":"))));
        Assert.Equal(JobStatus.Running, (await _t.GetJob(jobId)).Status);
    }

    [Fact]
    public async Task A_result_for_a_cancelled_job_is_ignored()
    {
        var jobId = await _t.UploadAndDispatch(3);
        await _t.InDb(db => db.Jobs.Where(j => j.Id == jobId).ExecuteUpdateAsync(s => s.SetProperty(j => j.Status, JobStatus.Cancelled)));

        var outcome = await _t.Handle(ResultTestSupport.Completed(jobId, 1, 2, 1));

        Assert.Equal(ResultOutcome.JobCancelled, outcome);
        var job = await _t.GetJob(jobId);
        Assert.Equal(JobStatus.Cancelled, job.Status);
        Assert.Equal(0, job.ProcessedRows);
        Assert.Equal(ChunkStatus.Dispatched, Assert.Single(await _t.GetChunks(jobId)).Status);
        Assert.Equal(0, await _t.InDb(db => db.JobResults.CountAsync(r => r.JobId == jobId)));
        Assert.Equal(0, await _t.InDb(db => db.JobErrors.CountAsync(r => r.JobId == jobId)));
    }

    [Fact]
    public async Task A_result_for_an_unknown_job_or_chunk_is_skipped()
    {
        var jobId = await _t.UploadAndDispatch(3);

        Assert.Equal(ResultOutcome.UnknownChunk, await _t.Handle(ResultTestSupport.Completed(Guid.NewGuid(), 1, 1, 0)));
        Assert.Equal(ResultOutcome.UnknownChunk, await _t.Handle(ResultTestSupport.Completed(jobId, 99, 1, 0)));

        var job = await _t.GetJob(jobId);
        Assert.Equal(JobStatus.Running, job.Status);
        Assert.Equal(0, job.ProcessedRows);
        Assert.Equal(0, await _t.InDb(db => db.JobResults.CountAsync(r => r.JobId == jobId)));
    }

    [Fact]
    public async Task A_chunk_that_was_never_dispatched_is_not_completed_by_a_result()
    {
        var jobId = await _t.UploadAndDispatch(10_001);
        var pending = (await _t.GetChunks(jobId)).Single(c => c.Status == ChunkStatus.Pending);

        var outcome = await _t.Handle(ResultTestSupport.Completed(jobId, pending.ChunkNo, 1, 0));

        Assert.Equal(ResultOutcome.UnknownChunk, outcome);
        Assert.Equal(ChunkStatus.Pending, (await _t.GetChunks(jobId)).Single(c => c.ChunkNo == pending.ChunkNo).Status);
    }

    private static string RawResult(Guid jobId, params (int Line, string CustomerId, string PlanName)[] rows)
        => System.Text.Json.JsonSerializer.Serialize(new
        {
            id = Guid.NewGuid(), type = "BatchChunkCompleted", version = 1, timestamp = DateTimeOffset.UtcNow, correlationId = (string?)null,
            data = new
            {
                jobId, chunkNo = 1,
                results = rows.Select(r => new
                {
                    line = r.Line, customerId = r.CustomerId, planId = Guid.Parse("5eed0000-0000-0000-0000-000000000001"),
                    planVersionNo = 1, planName = r.PlanName, total = 1234567.89m, taxGroupVersionNo = 1
                }),
                errors = Array.Empty<object>()
            }
        });

    [Fact]
    public async Task Texts_and_amounts_are_stored_exactly_as_they_arrive()
    {
        var jobId = await _t.UploadAndDispatch(3);
        var customer = "a,\"b\"\nc ščđćž ñ 日本";
        var longest = new string('x', 100);

        await _t.Handle(RawResult(jobId, (2, customer, "Plan \"Plus\", ž"), (3, longest, longest)));

        var rows = await _t.InDb(db => db.JobResults.AsNoTracking().Where(r => r.JobId == jobId).OrderBy(r => r.Line).ToListAsync());
        Assert.Equal([customer, longest], rows.Select(r => r.CustomerId));
        Assert.Equal(["Plan \"Plus\", ž", longest], rows.Select(r => r.PlanName));
        Assert.All(rows, r => Assert.Equal(1234567.89m, r.Total));
        Assert.Equal(2, (await _t.GetJob(jobId)).ProcessedRows);
    }

    [Fact]
    public async Task A_failure_while_writing_the_rows_rolls_back_the_whole_chunk()
    {
        var jobId = await _t.UploadAndDispatch(3);

        // The same line twice: the second row breaks the primary key in the middle of the COPY.
        await Assert.ThrowsAnyAsync<Exception>(() => _t.Handle(RawResult(jobId, (2, "c2", "Standard"), (2, "c2 again", "Standard"))));

        var job = await _t.GetJob(jobId);
        Assert.Equal(JobStatus.Running, job.Status);
        Assert.Equal(0, job.ProcessedRows);
        Assert.Equal(ChunkStatus.Dispatched, Assert.Single(await _t.GetChunks(jobId)).Status); // the chunk update is rolled back too
        Assert.Equal(0, await _t.InDb(db => db.JobResults.CountAsync(r => r.JobId == jobId)));

        // And the chunk can still be completed by a correct message.
        Assert.Equal(ResultOutcome.Applied, await _t.Handle(ResultTestSupport.Completed(jobId, 1, 3, 0)));
        Assert.Equal(JobStatus.Completed, (await _t.GetJob(jobId)).Status);
    }

    [Fact]
    public async Task Other_event_types_are_skipped_and_unreadable_messages_are_poison()
    {
        var jobId = await _t.UploadAndDispatch(3);

        Assert.Equal(ResultOutcome.SkippedUnknownType,
            await _t.Handle(ResultTestSupport.Completed(jobId, 1, 1, 0, type: "SomethingElse")));
        await Assert.ThrowsAsync<PoisonMessageException>(() => _t.Handle("not json"));
        await Assert.ThrowsAsync<PoisonMessageException>(() => _t.Handle(ResultTestSupport.Completed(jobId, 1, 1, 0, version: 2)));
        Assert.Equal(JobStatus.Running, (await _t.GetJob(jobId)).Status);
    }
}
