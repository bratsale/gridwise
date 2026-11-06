using System.Text;
using Confluent.Kafka;
using GridWise.Recommendation.Data;
using GridWise.Recommendation.Events;
using GridWise.Recommendation.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace GridWise.Recommendation.IntegrationTests;

[Collection("recommendation")]
public class DlqReprocessorTests(RecommendationFactory factory, KafkaFixture kafka) : IClassFixture<KafkaFixture>
{
    [Fact]
    public async Task Failed_messages_go_back_to_the_end_of_the_dlq_with_a_higher_attempt()
    {
        var validPlan = Guid.NewGuid();
        var badPlan = Guid.NewGuid();
        var badJson = CatalogEventHandlerTests.PlanEnvelope(badPlan, envelopeVersion: 99);

        using var producer = new ProducerBuilder<string, string>(new ProducerConfig { BootstrapServers = kafka.BootstrapServers }).Build();
        await producer.ProduceAsync(Topics.RecommendationDlq,
            new Message<string, string> { Key = validPlan.ToString(), Value = CatalogEventHandlerTests.PlanEnvelope(validPlan) });
        await producer.ProduceAsync(Topics.RecommendationDlq,
            new Message<string, string> { Key = badPlan.ToString(), Value = badJson });

        var first = await Run(producer);
        Assert.Equal(new DlqResult(1, 1), first);
        Assert.Equal(1, await CountVersions(validPlan));
        Assert.Equal(0, await CountVersions(badPlan));

        var messages = ReadDlq();
        Assert.Equal(3, messages.Count);
        var requeued = messages[^1];
        Assert.Equal(badPlan.ToString(), requeued.Message.Key);
        Assert.Equal(badJson, requeued.Message.Value);
        Assert.Equal("1", Header(requeued, "attempt"));
        Assert.Contains("99", Header(requeued, "error"));

        var second = await Run(producer);
        Assert.Equal(new DlqResult(0, 1), second);

        var after = ReadDlq();
        Assert.Equal(4, after.Count);
        Assert.Equal("2", Header(after[^1], "attempt"));
    }

    private Task<DlqResult> Run(IProducer<string, string> producer)
    {
        using var scope = factory.Services.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<DbContextOptions<RecommendationDbContext>>();
        return DlqReprocessor.RunAsync(
            () => new RecommendationDbContext(options),
            new ConsumerConfig { BootstrapServers = kafka.BootstrapServers }, producer, NullLogger.Instance,
            new CancellationTokenSource(TimeSpan.FromSeconds(60)).Token);
    }

    private async Task<int> CountVersions(Guid planId)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<RecommendationDbContext>()
            .PlanVersions.CountAsync(v => v.PlanId == planId);
    }

    private static string Header(ConsumeResult<string, string> message, string key) =>
        Encoding.UTF8.GetString(message.Message.Headers.GetLastBytes(key));

    // Reads the whole DLQ with its own group, so the reprocessor's offsets are not touched.
    private List<ConsumeResult<string, string>> ReadDlq()
    {
        using var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = kafka.BootstrapServers,
            GroupId = "test-" + Guid.NewGuid(),
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false
        }).Build();
        consumer.Subscribe(Topics.RecommendationDlq);

        var result = new List<ConsumeResult<string, string>>();
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            var message = consumer.Consume(TimeSpan.FromSeconds(2));
            if (message is null)
            {
                if (result.Count > 0) break;
                continue;
            }
            result.Add(message);
        }
        return result;
    }
}
