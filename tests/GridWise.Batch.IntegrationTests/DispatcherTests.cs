using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using GridWise.Batch.Data;
using GridWise.Batch.Domain;
using GridWise.Batch.Events;
using GridWise.Batch.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GridWise.Batch.IntegrationTests;

[Collection("batch-dispatch")]
public class DispatcherTests(BatchFactory factory)
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static string Csv(int rows)
        => "customer_id,consumption_kwh,tax_group\n" + string.Concat(Enumerable.Range(1, rows).Select(i => $"c{i},250.5,household\n"));

    private async Task<Guid> Upload(byte[] csv)
    {
        var form = new MultipartFormDataContent { { new ByteArrayContent(csv), "file", "customers.csv" } };
        var response = await factory.CreateClient().PostAsync("/jobs", form);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private Task<Guid> Upload(string csv) => Upload(Encoding.UTF8.GetBytes(csv));

    private async Task<int> Step(int chunkSize = 500, int windowSize = 20)
    {
        using var scope = factory.Services.CreateScope();
        return await BatchDispatcher.StepAsync(
            scope.ServiceProvider.GetRequiredService<BatchDbContext>(), factory.Time, chunkSize, windowSize, CancellationToken.None);
    }

    private async Task<(Job Job, List<Chunk> Chunks, List<OutboxMessage> Outbox)> Load(Guid jobId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BatchDbContext>();
        return (
            await db.Jobs.AsNoTracking().SingleAsync(j => j.Id == jobId),
            await db.Chunks.AsNoTracking().Where(c => c.JobId == jobId).OrderBy(c => c.ChunkNo).ToListAsync(),
            await db.OutboxMessages.AsNoTracking().Where(m => m.AggregateId.StartsWith(jobId + ":")).ToListAsync());
    }

    [Theory]
    [InlineData(1, new[] { 1 })]
    [InlineData(500, new[] { 500 })]
    [InlineData(501, new[] { 500, 1 })]
    public async Task Job_is_split_into_chunks_of_500_rows(int rows, int[] expectedRowCounts)
    {
        var jobId = await Upload(Csv(rows));

        await Step();

        var (job, chunks, outbox) = await Load(jobId);
        Assert.Equal(JobStatus.Running, job.Status);
        Assert.Equal(factory.Time.GetUtcNow(), job.StartedAt);
        Assert.Equal(expectedRowCounts, chunks.Select(c => c.RowCount));
        Assert.Equal(Enumerable.Range(1, expectedRowCounts.Length), chunks.Select(c => c.ChunkNo));
        Assert.All(chunks, c => Assert.Equal(ChunkStatus.Dispatched, c.Status));
        Assert.Equal(chunks.Count, outbox.Count);
    }

    [Fact]
    public async Task Line_numbers_count_the_header_and_blank_lines_and_ignore_a_bom()
    {
        // Lines: 1 header, 2 row, 3 blank, 4 row, 5 row, 6 blank, 7 row, 8 row.
        var csv = "customer_id,consumption_kwh,tax_group\r\nc1,1,a\r\n\r\nc2,2,a\r\nc3,3,a\r\n\r\nc4,4,a\r\nc5,5,a\r\n";
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray();
        var jobId = await Upload(bytes);

        await Step(chunkSize: 2);

        var (_, chunks, outbox) = await Load(jobId);
        Assert.Equal([(1, 2, 2), (2, 5, 2), (3, 8, 1)], chunks.Select(c => (c.ChunkNo, c.FirstLine, c.RowCount)));

        var events = outbox.Select(m => JsonSerializer.Deserialize<BatchChunkRequestedV1>(m.Payload, Web)!)
            .OrderBy(e => e.ChunkNo).ToList();
        Assert.Equal(
            [[(2, "c1,1,a"), (4, "c2,2,a")], [(5, "c3,3,a"), (7, "c4,4,a")], [(8, "c5,5,a")]],
            events.Select(e => e.Rows.Select(r => (r.Line, r.Text)).ToList()));
    }

    [Fact]
    public async Task Ten_thousand_rows_make_20_chunks_all_dispatched()
    {
        var jobId = await Upload(Csv(10_000));

        await Step();

        var (_, chunks, outbox) = await Load(jobId);
        Assert.Equal(20, chunks.Count);
        Assert.All(chunks, c => Assert.Equal(ChunkStatus.Dispatched, c.Status));
        Assert.Equal(20, outbox.Count);
    }

    [Fact]
    public async Task Ten_thousand_and_one_rows_dispatch_only_20_of_21_chunks()
    {
        var jobId = await Upload(Csv(10_001));

        await Step();

        var (_, chunks, outbox) = await Load(jobId);
        Assert.Equal(21, chunks.Count);
        Assert.Equal(20, chunks.Count(c => c.Status == ChunkStatus.Dispatched));
        var last = Assert.Single(chunks, c => c.Status == ChunkStatus.Pending);
        Assert.Equal(21, last.ChunkNo);
        Assert.Equal(1, last.RowCount);
        Assert.Null(last.DispatchedAt);
        Assert.Equal(20, outbox.Count);
    }

    [Fact]
    public async Task A_completed_chunk_frees_a_slot_for_the_next_one()
    {
        var jobId = await Upload(Csv(10_001));
        await Step();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BatchDbContext>();
            var first = await db.Chunks.SingleAsync(c => c.JobId == jobId && c.ChunkNo == 1);
            first.Status = ChunkStatus.Completed;
            first.CompletedAt = factory.Time.GetUtcNow();
            await db.SaveChangesAsync();
        }
        await Step();

        var (_, chunks, outbox) = await Load(jobId);
        Assert.All(chunks, c => Assert.NotEqual(ChunkStatus.Pending, c.Status));
        Assert.Equal(21, outbox.Count);
    }

    [Fact]
    public async Task Running_the_step_again_creates_no_duplicates()
    {
        var jobId = await Upload(Csv(10_001));
        await Step();
        var before = await Load(jobId);

        await Step();
        await Step();

        var after = await Load(jobId);
        Assert.Equal(before.Chunks.Count, after.Chunks.Count);
        Assert.Equal(before.Outbox.Count, after.Outbox.Count);
        Assert.Equal(before.Outbox.Select(m => m.Id).Order(), after.Outbox.Select(m => m.Id).Order());
    }

    [Fact]
    public async Task Outbox_row_has_the_shape_of_the_golden_file()
    {
        var jobId = await Upload(Csv(2));
        await Step();

        var (_, _, outbox) = await Load(jobId);
        var row = Assert.Single(outbox);
        Assert.Equal("batch-chunk", row.AggregateType);
        Assert.Equal($"{jobId}:1", row.AggregateId);
        Assert.Equal(EventTypes.BatchChunkRequested, row.EventType);
        Assert.Equal(1, row.EventVersion);
        Assert.Null(row.PublishedAt);

        var golden = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "events", "BatchChunkRequested.v1.json"));
        Assert.Equal(Shape(JsonDocument.Parse(golden).RootElement), Shape(JsonDocument.Parse(row.Payload).RootElement));
        Assert.Equal(jobId, JsonDocument.Parse(row.Payload).RootElement.GetProperty("jobId").GetGuid());
    }

    [Fact]
    public async Task Only_published_outbox_rows_older_than_7_days_are_deleted()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BatchDbContext>();
        var now = factory.Time.GetUtcNow();

        OutboxMessage Row(DateTimeOffset? publishedAt) => new()
        {
            Id = Guid.CreateVersion7(), AggregateType = "batch-chunk", AggregateId = "x", EventType = "t",
            EventVersion = 1, Payload = "{}", OccurredAt = now.AddDays(-9), PublishedAt = publishedAt
        };
        var old = Row(now.AddDays(-8));
        var recent = Row(now.AddDays(-1));
        var unpublished = Row(null);
        db.OutboxMessages.AddRange(old, recent, unpublished);
        await db.SaveChangesAsync();

        Assert.Equal(1, await RetentionCleanup.DeletePublishedOutboxAsync(db, now, CancellationToken.None));
        Assert.Equal(0, await db.OutboxMessages.CountAsync(m => m.Id == old.Id));
        Assert.Equal(1, await db.OutboxMessages.CountAsync(m => m.Id == recent.Id));
        Assert.Equal(1, await db.OutboxMessages.CountAsync(m => m.Id == unpublished.Id));
    }

    // Property names and value kinds, recursively; an array is described by its first element.
    private static string Shape(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.Object => "{" + string.Join(",", e.EnumerateObject().OrderBy(p => p.Name).Select(p => p.Name + ":" + Shape(p.Value))) + "}",
        JsonValueKind.Array => "[" + (e.GetArrayLength() > 0 ? Shape(e[0]) : "") + "]",
        JsonValueKind.True or JsonValueKind.False => "bool",
        _ => e.ValueKind.ToString()
    };
}
