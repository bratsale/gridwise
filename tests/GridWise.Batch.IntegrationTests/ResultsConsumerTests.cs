using System.Text;
using Confluent.Kafka;
using GridWise.Batch.Domain;
using GridWise.Batch.Events;
using GridWise.Batch.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace GridWise.Batch.IntegrationTests;

[Collection("batch-results")]
public class ResultsConsumerTests(BatchFactory factory, KafkaFixture kafka) : IClassFixture<KafkaFixture>
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(40);

    private readonly ResultTestSupport _t = new(factory);

    // Calls the real handler unless `failure` returns an exception for the call number.
    private sealed class FlakyHandler(
        Data.BatchDbContext db, TimeProvider time, Func<int, Exception?> failure, int[] calls) : BatchResultHandler(db, time)
    {
        public override Task<ResultOutcome> HandleAsync(string json, CancellationToken ct)
            => failure(Interlocked.Increment(ref calls[0])) is { } ex ? throw ex : base.HandleAsync(json, ct);
    }

    private async Task Produce(string key, string value)
    {
        using var producer = new ProducerBuilder<string, string>(new ProducerConfig { BootstrapServers = kafka.BootstrapServers }).Build();
        await producer.ProduceAsync(Topics.BatchResults, new Message<string, string> { Key = key, Value = value });
    }

    private async Task<IAsyncDisposable> Run(Func<int, Exception?>? failure = null, int[]? calls = null)
    {
        IServiceScopeFactory scopes = factory.Services.GetRequiredService<IServiceScopeFactory>();
        if (failure is not null)
        {
            var calls2 = calls ?? new int[1];
            var flaky = factory.WithWebHostBuilder(b => b.ConfigureServices(s => s.AddScoped<BatchResultHandler>(sp =>
                new FlakyHandler(sp.GetRequiredService<Data.BatchDbContext>(), sp.GetRequiredService<TimeProvider>(), failure, calls2))));
            scopes = flaky.Services.GetRequiredService<IServiceScopeFactory>();
        }

        var config = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["Kafka:BootstrapServers"] = kafka.BootstrapServers }).Build();
        var consumer = new BatchResultsConsumer(scopes, config, TimeProvider.System, NullLogger<BatchResultsConsumer>.Instance);
        await consumer.StartAsync(CancellationToken.None);
        return new Running(consumer);
    }

    private sealed class Running(BatchResultsConsumer consumer) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => new(consumer.StopAsync(CancellationToken.None));
    }

    private async Task Eventually(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (!await condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Condition was not met in time.");
            await Task.Delay(250);
        }
    }

    private ConsumeResult<string, string>? ConsumeDlq(string key, TimeSpan timeout)
    {
        using var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = kafka.BootstrapServers,
            GroupId = Guid.NewGuid().ToString(),
            AutoOffsetReset = AutoOffsetReset.Earliest
        }).Build();
        consumer.Subscribe(Topics.BatchDlq);

        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var result = consumer.Consume(TimeSpan.FromMilliseconds(500));
            if (result?.Message.Key == key)
                return result;
        }
        return null;
    }

    private static string Header(ConsumeResult<string, string> result, string name)
        => Encoding.UTF8.GetString(result.Message.Headers.GetLastBytes(name));

    [Fact]
    public async Task A_result_from_kafka_is_applied_and_the_job_completes()
    {
        var jobId = await _t.UploadAndDispatch(3);
        await Produce(jobId.ToString(), ResultTestSupport.Completed(jobId, 1, 2, 1));

        await using var running = await Run();
        await Eventually(async () => (await _t.GetJob(jobId)).Status == JobStatus.Completed);

        var job = await _t.GetJob(jobId);
        Assert.Equal((2, 1), (job.ProcessedRows, job.FailedRows));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("version")]
    public async Task Unreadable_message_or_unknown_version_goes_to_batch_dlq_and_is_committed(string kind)
    {
        var jobId = await _t.UploadAndDispatch(3);
        var json = kind == "version" ? ResultTestSupport.Completed(jobId, 1, 1, 0, version: 99) : "not json";
        var key = $"dlq-{Guid.NewGuid()}";
        await Produce(key, json);

        await using var running = await Run();
        var dead = ConsumeDlq(key, Timeout);

        Assert.NotNull(dead);
        Assert.Equal(json, dead.Message.Value);
        Assert.Equal(Topics.BatchResults, Header(dead, "source-topic"));
        Assert.True(int.TryParse(Header(dead, "source-partition"), out _));
        Assert.True(long.TryParse(Header(dead, "source-offset"), out _));
        Assert.NotEmpty(Header(dead, "error"));
        Assert.Equal(JobStatus.Running, (await _t.GetJob(jobId)).Status);
    }

    [Fact]
    public async Task Database_outage_is_retried_without_commit_and_not_dead_lettered()
    {
        var jobId = await _t.UploadAndDispatch(3);
        await Produce(jobId.ToString(), ResultTestSupport.Completed(jobId, 1, 2, 1));
        var calls = new int[1];

        // The first call fails like a lost database connection; the same message must be tried again (after 5 s).
        await using var running = await Run(call => call == 1 ? new NpgsqlException("connection lost") : null, calls);
        await Eventually(async () => (await _t.GetJob(jobId)).Status == JobStatus.Completed);

        Assert.True(calls[0] >= 2);
        Assert.Equal(2, (await _t.GetJob(jobId)).ProcessedRows); // applied once
        Assert.Null(ConsumeDlq(jobId.ToString(), TimeSpan.FromSeconds(3)));
    }

    [Fact]
    public async Task Other_errors_are_tried_3_times_and_then_dead_lettered()
    {
        var jobId = await _t.UploadAndDispatch(3);
        await Produce(jobId.ToString(), ResultTestSupport.Completed(jobId, 1, 2, 1));
        var calls = new int[1];

        await using var running = await Run(_ => new InvalidOperationException("boom"), calls);
        var dead = ConsumeDlq(jobId.ToString(), Timeout);

        Assert.NotNull(dead);
        Assert.Equal("boom", Header(dead, "error"));
        Assert.Equal(3, calls[0]);
        Assert.Equal(JobStatus.Running, (await _t.GetJob(jobId)).Status);
    }
}
