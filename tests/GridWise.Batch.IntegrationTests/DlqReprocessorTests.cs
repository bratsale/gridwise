using System.Text;
using Confluent.Kafka;
using GridWise.Batch.Data;
using GridWise.Batch.Domain;
using GridWise.Batch.Events;
using GridWise.Batch.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace GridWise.Batch.IntegrationTests;

[Collection("batch-results")]
public class DlqReprocessorTests(BatchFactory factory, KafkaFixture kafka) : IClassFixture<KafkaFixture>
{
    private readonly ResultTestSupport _t = new(factory);

    private static Message<string, string> Dead(string key, string value, string? sourceTopic)
    {
        var headers = new Headers();
        if (sourceTopic is not null)
            headers.Add("source-topic", Encoding.UTF8.GetBytes(sourceTopic));
        return new Message<string, string> { Key = key, Value = value, Headers = headers };
    }

    private async Task<DlqResult> Run(IProducer<string, string> producer)
    {
        using var scope = factory.Services.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<DbContextOptions<BatchDbContext>>();
        return await DlqReprocessor.RunAsync(
            () => new BatchDbContext(options),
            new ConsumerConfig { BootstrapServers = kafka.BootstrapServers }, producer, NullLogger.Instance,
            new CancellationTokenSource(TimeSpan.FromSeconds(60)).Token);
    }

    // The messages with the key, with the test's own group so the reprocessor's offsets are not touched.
    private List<ConsumeResult<string, string>> ReadDlq(string key)
    {
        using var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = kafka.BootstrapServers,
            GroupId = "test-" + Guid.NewGuid(),
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false
        }).Build();
        consumer.Subscribe(Topics.BatchDlq);

        var found = new List<ConsumeResult<string, string>>();
        var deadline = DateTime.UtcNow.AddSeconds(20);
        var lastMessage = DateTime.UtcNow;
        while (DateTime.UtcNow < deadline && (found.Count == 0 || DateTime.UtcNow - lastMessage < TimeSpan.FromSeconds(3)))
        {
            var message = consumer.Consume(TimeSpan.FromMilliseconds(500));
            if (message is null)
                continue;
            lastMessage = DateTime.UtcNow;
            if (message.Message.Key == key)
                found.Add(message);
        }
        return found;
    }

    private static string Header(ConsumeResult<string, string> message, string key)
        => Encoding.UTF8.GetString(message.Message.Headers.GetLastBytes(key));

    [Fact]
    public async Task A_fixed_message_is_processed_and_a_broken_one_is_requeued()
    {
        var jobId = await _t.UploadAndDispatch(3);
        var fixedJson = ResultTestSupport.Completed(jobId, 1, 2, 1); // as if the cause of its failure was fixed
        var brokenKey = "broken-" + Guid.NewGuid();

        using var producer = new ProducerBuilder<string, string>(new ProducerConfig { BootstrapServers = kafka.BootstrapServers }).Build();
        await producer.ProduceAsync(Topics.BatchDlq, Dead(jobId.ToString(), fixedJson, Topics.BatchResults));
        await producer.ProduceAsync(Topics.BatchDlq, Dead(brokenKey, "not json", Topics.BatchResults));

        var result = await Run(producer);

        Assert.True(result.Reprocessed >= 1 && result.FailedAgain >= 1);
        var job = await _t.GetJob(jobId);
        Assert.Equal(JobStatus.Completed, job.Status);
        Assert.Equal((2, 1), (job.ProcessedRows, job.FailedRows));

        var requeued = ReadDlq(brokenKey).Last();
        Assert.Equal("not json", requeued.Message.Value);
        Assert.True(int.Parse(Header(requeued, "attempt")) >= 1);
        Assert.Contains("not valid JSON", Header(requeued, "error"));
        Assert.Equal(Topics.BatchResults, Header(requeued, "source-topic"));
    }

    [Theory]
    [InlineData("something.else")]
    [InlineData(null)]
    public async Task Message_with_an_unknown_or_missing_source_topic_is_requeued_not_skipped(string? sourceTopic)
    {
        var key = "unknown-" + Guid.NewGuid();
        using var producer = new ProducerBuilder<string, string>(new ProducerConfig { BootstrapServers = kafka.BootstrapServers }).Build();
        await producer.ProduceAsync(Topics.BatchDlq, Dead(key, "{}", sourceTopic));

        var result = await Run(producer);

        Assert.True(result.FailedAgain >= 1);
        var requeued = ReadDlq(key).Last();
        Assert.True(int.Parse(Header(requeued, "attempt")) >= 1);
        Assert.Contains("Cannot reprocess", Header(requeued, "error"));
    }
}
