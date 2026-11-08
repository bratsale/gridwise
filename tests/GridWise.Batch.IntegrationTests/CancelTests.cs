using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GridWise.Batch.Data;
using GridWise.Batch.Domain;
using GridWise.Batch.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GridWise.Batch.IntegrationTests;

[Collection("batch-results")]
public class CancelTests(BatchFactory factory)
{
    private readonly ResultTestSupport _t = new(factory);

    private async Task<Guid> UploadOnly(int rows)
    {
        var form = new MultipartFormDataContent { { new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(ResultTestSupport.Csv(rows))), "file", "c.csv" } };
        var response = await factory.CreateClient().PostAsync("/jobs", form);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private Task<HttpResponseMessage> Cancel(Guid jobId) => factory.CreateClient().PostAsync($"/jobs/{jobId}/cancel", null);

    private Task<int> OutboxCount(Guid jobId) => _t.InDb(db => db.OutboxMessages.CountAsync(m => m.AggregateId.StartsWith(jobId + ":")));

    [Fact]
    public async Task Cancelling_a_pending_job_means_nothing_is_ever_dispatched()
    {
        var jobId = await UploadOnly(1200);
        factory.Time.Advance(TimeSpan.FromSeconds(5));

        var response = await Cancel(jobId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Cancelled", body.GetProperty("status").GetString());
        Assert.Equal(factory.Time.GetUtcNow(), body.GetProperty("finishedAt").GetDateTimeOffset());

        await _t.Step();
        await _t.Step();

        var job = await _t.GetJob(jobId);
        Assert.Equal(JobStatus.Cancelled, job.Status);
        Assert.Null(job.StartedAt);
        Assert.Empty(await _t.GetChunks(jobId));
        Assert.Equal(0, await OutboxCount(jobId));
    }

    [Fact]
    public async Task Cancelling_a_running_job_stops_dispatching_ignores_later_results_and_freezes_the_counters()
    {
        var jobId = await _t.UploadAndDispatch(10_001);
        await _t.Handle(ResultTestSupport.Completed(jobId, 1, 499, 1)); // one chunk done, so a window slot is free
        var before = await _t.GetJob(jobId);
        Assert.Equal((499, 1), (before.ProcessedRows, before.FailedRows));

        var response = await Cancel(jobId);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await _t.Step(); // would dispatch chunk 21 if the job were still Running
        Assert.Equal(20, await OutboxCount(jobId));
        Assert.Equal(ChunkStatus.Pending, (await _t.GetChunks(jobId))[20].Status);

        // A result of an in-flight chunk arrives after the cancel.
        Assert.Equal(ResultOutcome.JobCancelled, await _t.Handle(ResultTestSupport.Completed(jobId, 2, 500, 0, firstLine: 502)));

        var job = await _t.GetJob(jobId);
        Assert.Equal(JobStatus.Cancelled, job.Status);
        Assert.Equal((499, 1), (job.ProcessedRows, job.FailedRows));
        Assert.Equal(ChunkStatus.Dispatched, (await _t.GetChunks(jobId))[1].Status);
        Assert.Equal(20, await OutboxCount(jobId));
    }

    [Fact]
    public async Task The_dispatch_transaction_checks_the_status_again_under_the_lock()
    {
        var jobId = await _t.UploadAndDispatch(10_001);
        await _t.Handle(ResultTestSupport.Completed(jobId, 1, 500, 0)); // a free slot and a Pending chunk
        await Cancel(jobId);

        // As if the dispatcher had read the job as Running just before the cancel committed.
        int dispatched;
        using (var scope = factory.Services.CreateScope())
            dispatched = await BatchDispatcher.DispatchJobAsync(
                scope.ServiceProvider.GetRequiredService<BatchDbContext>(), jobId, factory.Time, 20, CancellationToken.None);

        Assert.Equal(0, dispatched);
        Assert.Equal(ChunkStatus.Pending, (await _t.GetChunks(jobId))[20].Status);
        Assert.Equal(20, await OutboxCount(jobId));
    }

    [Fact]
    public async Task Cancelling_twice_is_fine_and_keeps_the_first_finish_time()
    {
        var jobId = await UploadOnly(3);
        var first = await (await Cancel(jobId)).Content.ReadFromJsonAsync<JsonElement>();
        factory.Time.Advance(TimeSpan.FromMinutes(1));

        var secondResponse = await Cancel(jobId);

        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        var second = await secondResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Cancelled", second.GetProperty("status").GetString());
        Assert.Equal(first.GetProperty("finishedAt").GetDateTimeOffset(), second.GetProperty("finishedAt").GetDateTimeOffset());
    }

    [Fact]
    public async Task Cancelling_a_completed_job_is_a_conflict()
    {
        var jobId = await _t.UploadAndDispatch(3);
        await _t.Handle(ResultTestSupport.Completed(jobId, 1, 3, 0));

        var response = await Cancel(jobId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(JobStatus.Completed, (await _t.GetJob(jobId)).Status);
    }

    [Fact]
    public async Task Cancelling_a_failed_job_is_a_conflict()
    {
        var jobId = await UploadOnly(3);
        await _t.InDb(db => db.Jobs.Where(j => j.Id == jobId).ExecuteUpdateAsync(s => s.SetProperty(j => j.Status, JobStatus.Failed)));

        Assert.Equal(HttpStatusCode.Conflict, (await Cancel(jobId)).StatusCode);
    }

    [Fact]
    public async Task Cancelling_an_unknown_job_is_404()
        => Assert.Equal(HttpStatusCode.NotFound, (await Cancel(Guid.NewGuid())).StatusCode);
}
