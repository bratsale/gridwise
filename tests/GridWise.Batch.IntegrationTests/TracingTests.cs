using System.Text;
using Confluent.Kafka;
using GridWise.Batch.Data;
using GridWise.Batch.Events;
using GridWise.Batch.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace GridWise.Batch.IntegrationTests;

[Collection("batch-results")]
public class TracingTests(BatchFactory factory, KafkaFixture kafka) : IClassFixture<KafkaFixture>
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(40);

    private readonly ResultTestSupport _t = new(factory);

    [Fact]
    public async Task Dispatched_chunk_gets_a_trace_and_its_outbox_row_stores_it()
    {
        using var traces = new TraceCapture();

        var jobId = await _t.UploadAndDispatch(3);

        var dispatch = traces.Stopped.Single(a => a.OperationName == "dispatch chunk" && (string?)a.GetTagItem("job.id") == jobId.ToString());
        using var scope = factory.Services.CreateScope();
        var row = await scope.ServiceProvider.GetRequiredService<BatchDbContext>()
            .OutboxMessages.AsNoTracking().SingleAsync(m => m.AggregateId == $"{jobId}:1");
        Assert.Equal(traces.TraceParent, dispatch.ParentId); // a child of the test's root span
        Assert.Equal(dispatch.Id, row.TraceParent);
    }

    [Fact]
    public async Task Results_consumer_continues_the_trace_of_the_result_message()
    {
        using var traces = new TraceCapture();
        var jobId = await _t.UploadAndDispatch(3);
        using (var producer = new ProducerBuilder<string, string>(new ProducerConfig { BootstrapServers = kafka.BootstrapServers }).Build())
        {
            await producer.ProduceAsync(Topics.BatchResults, new Message<string, string>
            {
                Key = jobId.ToString(),
                Value = ResultTestSupport.Completed(jobId, 1, 2, 1),
                Headers = new Headers { { "traceparent", Encoding.UTF8.GetBytes(traces.TraceParent) } }
            });
        }

        var config = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["Kafka:BootstrapServers"] = kafka.BootstrapServers }).Build();
        var consumer = new BatchResultsConsumer(
            factory.Services.GetRequiredService<IServiceScopeFactory>(), config, TimeProvider.System, NullLogger<BatchResultsConsumer>.Instance);
        await consumer.StartAsync(CancellationToken.None);
        try
        {
            var deadline = DateTime.UtcNow + Timeout;
            while (!traces.Stopped.Any(a => a.OperationName == "process"))
            {
                Assert.True(DateTime.UtcNow < deadline, "No process span arrived in time.");
                await Task.Delay(250);
            }
        }
        finally
        {
            await consumer.StopAsync(CancellationToken.None);
        }

        var process = traces.Stopped.Single(a => a.OperationName == "process");
        Assert.Equal(traces.TraceParent, process.ParentId);
        Assert.Equal(traces.TraceId, process.TraceId.ToHexString());
    }
}
