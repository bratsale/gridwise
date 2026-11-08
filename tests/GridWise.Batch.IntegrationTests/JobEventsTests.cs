using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace GridWise.Batch.IntegrationTests;

[Collection("batch-results")]
public class JobEventsTests(BatchFactory factory)
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(30);

    private readonly ResultTestSupport _t = new(factory);

    // The raw text of the stream, read block by block (an event ends with an empty line).
    private sealed class EventReader(StreamReader reader)
    {
        public async Task<string?> NextBlockAsync(CancellationToken ct)
        {
            var block = new System.Text.StringBuilder();
            while (await reader.ReadLineAsync(ct) is { } line)
            {
                if (line.Length == 0)
                    return block.ToString();
                block.AppendLine(line);
            }
            return block.Length > 0 ? block.ToString() : null; // null = the server closed the stream
        }

        public async Task<(string Id, JsonElement Data)?> NextEventAsync(CancellationToken ct)
        {
            while (await NextBlockAsync(ct) is { } block)
            {
                if (block.StartsWith(':'))
                    continue; // heartbeat comment
                var lines = block.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                return (lines.Single(l => l.StartsWith("id: ")), JsonDocument.Parse(lines.Single(l => l.StartsWith("data: "))[6..]).RootElement.Clone());
            }
            return null;
        }
    }

    private static async Task<(HttpResponseMessage Response, EventReader Reader)> Open(HttpClient client, Guid jobId, CancellationToken ct)
    {
        var response = await client.GetAsync($"/jobs/{jobId}/events", HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        return (response, new EventReader(new StreamReader(await response.Content.ReadAsStreamAsync(ct))));
    }

    [Fact]
    public async Task Sends_the_current_state_at_once_then_an_event_for_each_change_and_closes_after_the_final_one()
    {
        var jobId = await _t.UploadAndDispatch(1200); // 3 chunks
        using var cts = new CancellationTokenSource(Limit);
        var (response, events) = await Open(factory.CreateClient(), jobId, cts.Token);
        using var _ = response;

        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("no", response.Headers.GetValues("X-Accel-Buffering").Single());

        var first = (await events.NextEventAsync(cts.Token))!.Value;
        Assert.Equal("id: 1", first.Id);
        Assert.Equal("Running", first.Data.GetProperty("status").GetString());
        Assert.Equal(3, first.Data.GetProperty("totalChunks").GetInt32());
        Assert.Equal(0, first.Data.GetProperty("completedChunks").GetInt32());

        await _t.Handle(ResultTestSupport.Completed(jobId, 1, 499, 1));
        var second = (await events.NextEventAsync(cts.Token))!.Value;
        Assert.Equal("id: 2", second.Id);
        Assert.Equal(1, second.Data.GetProperty("completedChunks").GetInt32());
        Assert.Equal(499, second.Data.GetProperty("processedRows").GetInt32());
        Assert.Equal(1, second.Data.GetProperty("failedRows").GetInt32());

        await _t.Handle(ResultTestSupport.Completed(jobId, 2, 500, 0, firstLine: 502));
        await _t.Handle(ResultTestSupport.Completed(jobId, 3, 200, 0, firstLine: 1002));

        (string Id, JsonElement Data) last = second;
        while (await events.NextEventAsync(cts.Token) is { } next)
            last = next;

        Assert.Equal("Completed", last.Data.GetProperty("status").GetString());
        Assert.Equal(3, last.Data.GetProperty("completedChunks").GetInt32());
        Assert.Equal(1199, last.Data.GetProperty("processedRows").GetInt32());
        Assert.Null(await events.NextEventAsync(cts.Token)); // closed
    }

    [Fact]
    public async Task A_job_that_is_already_finished_gets_one_event_and_the_stream_closes()
    {
        var jobId = await _t.UploadAndDispatch(3);
        await _t.Handle(ResultTestSupport.Completed(jobId, 1, 3, 0));
        using var cts = new CancellationTokenSource(Limit);
        var (response, events) = await Open(factory.CreateClient(), jobId, cts.Token);
        using var _ = response;

        var only = (await events.NextEventAsync(cts.Token))!.Value;

        Assert.Equal("Completed", only.Data.GetProperty("status").GetString());
        Assert.Null(await events.NextEventAsync(cts.Token));
    }

    [Fact]
    public async Task Cancelling_sends_a_final_cancelled_event()
    {
        var jobId = await _t.UploadAndDispatch(1200);
        using var cts = new CancellationTokenSource(Limit);
        var (response, events) = await Open(factory.CreateClient(), jobId, cts.Token);
        using var _ = response;
        await events.NextEventAsync(cts.Token);

        await factory.CreateClient().PostAsync($"/jobs/{jobId}/cancel", null);

        var final = (await events.NextEventAsync(cts.Token))!.Value;
        Assert.Equal("Cancelled", final.Data.GetProperty("status").GetString());
        Assert.Null(await events.NextEventAsync(cts.Token));
    }

    [Fact]
    public async Task A_quiet_stream_sends_a_heartbeat_comment()
    {
        var jobId = await _t.UploadAndDispatch(1200);
        using var quick = factory.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, c) =>
            c.AddInMemoryCollection(new Dictionary<string, string?> { ["Batch:SseHeartbeatSeconds"] = "1" })));
        using var cts = new CancellationTokenSource(Limit);
        var (response, events) = await Open(quick.CreateClient(), jobId, cts.Token);
        using var _ = response;
        await events.NextEventAsync(cts.Token);

        var block = await events.NextBlockAsync(cts.Token);

        Assert.StartsWith(": heartbeat", block);
    }

    [Fact]
    public async Task An_open_stream_ends_when_the_application_is_stopping()
    {
        var jobId = await _t.UploadAndDispatch(1200);
        // Its own host, so stopping it does not affect the other tests.
        using var own = factory.WithWebHostBuilder(_ => { });
        using var cts = new CancellationTokenSource(Limit);
        var (response, events) = await Open(own.CreateClient(), jobId, cts.Token);
        using var _ = response;
        await events.NextEventAsync(cts.Token);

        own.Services.GetRequiredService<IHostApplicationLifetime>().StopApplication();

        Assert.Null(await events.NextEventAsync(cts.Token)); // closed by the server, not by the client
    }

    [Fact]
    public async Task Unknown_job_is_404()
    {
        var response = await factory.CreateClient().GetAsync($"/jobs/{Guid.NewGuid()}/events");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
