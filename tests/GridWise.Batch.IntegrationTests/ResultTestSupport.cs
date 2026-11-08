using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using GridWise.Batch.Data;
using GridWise.Batch.Domain;
using GridWise.Batch.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GridWise.Batch.IntegrationTests;

// Shared by the result tests: creates a job through the API, lets the dispatcher run, builds BatchChunkCompleted messages.
internal class ResultTestSupport(BatchFactory factory)
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    public static string Csv(int rows)
        => "customer_id,consumption_kwh,tax_group\n" + string.Concat(Enumerable.Range(1, rows).Select(i => $"c{i},250.5,household\n"));

    public async Task<Guid> UploadAndDispatch(int rows)
    {
        var form = new MultipartFormDataContent { { new ByteArrayContent(Encoding.UTF8.GetBytes(Csv(rows))), "file", "c.csv" } };
        var response = await factory.CreateClient().PostAsync("/jobs", form);
        response.EnsureSuccessStatusCode();
        var jobId = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await Step();
        return jobId;
    }

    public async Task Step()
    {
        using var scope = factory.Services.CreateScope();
        await BatchDispatcher.StepAsync(
            scope.ServiceProvider.GetRequiredService<BatchDbContext>(), factory.Time, 500, 20, CancellationToken.None);
    }

    // Result lines are numbered from `firstLine`; error lines follow the results.
    public static string Completed(Guid jobId, int chunkNo, int results, int errors, int firstLine = 2, string type = "BatchChunkCompleted", int version = 1, string planName = "Standard")
        => JsonSerializer.Serialize(new
        {
            id = Guid.NewGuid(),
            type,
            version,
            timestamp = DateTimeOffset.UtcNow,
            correlationId = (string?)null,
            data = new
            {
                jobId,
                chunkNo,
                results = Enumerable.Range(0, results).Select(i => new
                {
                    line = firstLine + i, customerId = $"c{firstLine + i}", planId = Guid.Parse("5eed0000-0000-0000-0000-000000000001"),
                    planVersionNo = 1, planName, total = 24.10m, taxGroupVersionNo = 1
                }),
                errors = Enumerable.Range(0, errors).Select(i => new { line = firstLine + results + i, error = "consumption_kwh 'abc' is not a number." })
            }
        }, Web);

    public async Task<ResultOutcome> Handle(string json)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<BatchResultHandler>().HandleAsync(json, CancellationToken.None);
    }

    public async Task<T> InDb<T>(Func<BatchDbContext, Task<T>> query)
    {
        using var scope = factory.Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<BatchDbContext>());
    }

    public Task<Job> GetJob(Guid jobId) => InDb(db => db.Jobs.AsNoTracking().SingleAsync(j => j.Id == jobId));

    public Task<List<Chunk>> GetChunks(Guid jobId)
        => InDb(db => db.Chunks.AsNoTracking().Where(c => c.JobId == jobId).OrderBy(c => c.ChunkNo).ToListAsync());
}
