using System.Text;
using Confluent.Kafka;
using GridWise.Recommendation.Events;
using GridWise.Recommendation.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace GridWise.Recommendation.IntegrationTests;

[Collection("recommendation")]
public class TracingTests(RecommendationFactory factory, KafkaFixture kafka) : IClassFixture<KafkaFixture>
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task Consumer_continues_the_trace_of_the_message()
    {
        using var traces = new TraceCapture();
        var planId = Guid.NewGuid();
        await Produce(planId.ToString(), CatalogEventHandlerTests.PlanEnvelope(planId), traces.TraceParent);

        await using var running = await Run();
        await Eventually(() => traces.Stopped.Any(a => a.OperationName == "process"));

        var process = traces.Stopped.Single(a => a.OperationName == "process");
        Assert.Equal(traces.TraceParent, process.ParentId);
        Assert.Equal(traces.TraceId, process.TraceId.ToHexString());
        Assert.Equal(Topics.CatalogPlanEvents, process.GetTagItem("messaging.destination.name"));
    }

    [Fact]
    public async Task Dead_lettered_message_keeps_the_trace_for_reprocessing()
    {
        using var traces = new TraceCapture();
        var planId = Guid.NewGuid();
        await Produce(planId.ToString(), CatalogEventHandlerTests.PlanEnvelope(planId, envelopeVersion: 99), traces.TraceParent);

        await using var running = await Run();
        var dead = ConsumeDlq(planId.ToString());

        var header = Encoding.UTF8.GetString(dead.Message.Headers.GetLastBytes("traceparent"));
        Assert.StartsWith($"00-{traces.TraceId}-", header);
    }

    private async Task Produce(string key, string value, string traceParent)
    {
        using var producer = new ProducerBuilder<string, string>(new ProducerConfig { BootstrapServers = kafka.BootstrapServers }).Build();
        await producer.ProduceAsync(Topics.CatalogPlanEvents, new Message<string, string>
        {
            Key = key,
            Value = value,
            Headers = new Headers { { "traceparent", Encoding.UTF8.GetBytes(traceParent) } }
        });
    }

    private async Task<IAsyncDisposable> Run()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Kafka:BootstrapServers"] = kafka.BootstrapServers,
            ["Kafka:GroupId"] = Guid.NewGuid().ToString()
        }).Build();
        var consumer = new CatalogConsumer(
            factory.Services.GetRequiredService<IServiceScopeFactory>(), config,
            factory.Services.GetRequiredService<CatalogEventHandler>(), TimeProvider.System, NullLogger<CatalogConsumer>.Instance);
        await consumer.StartAsync(CancellationToken.None);
        return new Running(consumer);
    }

    private sealed class Running(CatalogConsumer consumer) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => new(consumer.StopAsync(CancellationToken.None));
    }

    private ConsumeResult<string, string> ConsumeDlq(string key)
    {
        using var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = kafka.BootstrapServers,
            GroupId = Guid.NewGuid().ToString(),
            AutoOffsetReset = AutoOffsetReset.Earliest
        }).Build();
        consumer.Subscribe(Topics.RecommendationDlq);

        var deadline = DateTime.UtcNow + Timeout;
        while (DateTime.UtcNow < deadline)
        {
            var result = consumer.Consume(TimeSpan.FromMilliseconds(500));
            if (result?.Message.Key == key)
                return result;
        }
        throw new TimeoutException($"No message with key {key} arrived in {Topics.RecommendationDlq}.");
    }

    private static async Task Eventually(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("Condition was not met in time.");
            await Task.Delay(250);
        }
    }
}
