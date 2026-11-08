using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using GridWise.Recommendation.Data;
using GridWise.Recommendation.Events;
using GridWise.Recommendation.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.Kafka;

namespace GridWise.Recommendation.IntegrationTests;

public class KafkaFixture : IAsyncLifetime
{
    private readonly KafkaContainer _kafka = new KafkaBuilder("apache/kafka:4.4.0").Build();

    public string BootstrapServers => _kafka.GetBootstrapAddress();

    public async Task InitializeAsync()
    {
        await _kafka.StartAsync();

        using var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = BootstrapServers }).Build();
        await admin.CreateTopicsAsync(
        [
            new TopicSpecification { Name = Topics.CatalogPlanEvents, NumPartitions = 3 },
            new TopicSpecification { Name = Topics.CatalogTaxGroupEvents, NumPartitions = 3 },
            new TopicSpecification { Name = Topics.RecommendationDlq, NumPartitions = 1 },
            new TopicSpecification { Name = Topics.RecommendationEvents, NumPartitions = 3 },
            new TopicSpecification { Name = Topics.BatchChunks, NumPartitions = 3 },
            new TopicSpecification { Name = Topics.BatchResults, NumPartitions = 3 }
        ]);
    }

    public Task DisposeAsync() => _kafka.DisposeAsync().AsTask();
}

[Collection("recommendation")]
public class CatalogConsumerTests(RecommendationFactory factory, KafkaFixture kafka) : IClassFixture<KafkaFixture>
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task Plan_event_fills_plan_versions()
    {
        var planId = Guid.NewGuid();
        await Produce(planId.ToString(), CatalogEventHandlerTests.PlanEnvelope(planId));

        await using var running = await Run();
        await Eventually(async () => await CountVersions(planId) == 1);

        using var scope = factory.Services.CreateScope();
        var row = await scope.ServiceProvider.GetRequiredService<RecommendationDbContext>()
            .PlanVersions.AsNoTracking().SingleAsync(v => v.PlanId == planId);
        Assert.Equal(2, JsonDocument.Parse(row.Tiers).RootElement.GetArrayLength());
    }

    [Fact]
    public async Task Same_message_twice_gives_one_row()
    {
        var planId = Guid.NewGuid();
        var json = CatalogEventHandlerTests.PlanEnvelope(planId);
        await Produce(planId.ToString(), json);
        await Produce(planId.ToString(), json);
        // Same key, same partition: once version 2 is applied, both copies of version 1 have been handled.
        await Produce(planId.ToString(), CatalogEventHandlerTests.PlanEnvelope(planId, versionNo: 2));

        await using var running = await Run();
        await Eventually(async () => await CountVersions(planId) == 2);

        using var scope = factory.Services.CreateScope();
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<RecommendationDbContext>()
            .PlanVersions.CountAsync(v => v.PlanId == planId && v.VersionNo == 1));
    }

    [Fact]
    public async Task Version_2_before_version_1_gives_both_rows()
    {
        var planId = Guid.NewGuid();
        await Produce(planId.ToString(), CatalogEventHandlerTests.PlanEnvelope(planId, versionNo: 2));
        await Produce(planId.ToString(), CatalogEventHandlerTests.PlanEnvelope(planId, versionNo: 1));

        await using var running = await Run();
        await Eventually(async () => await CountVersions(planId) == 2);
    }

    [Fact]
    public async Task Unknown_version_goes_to_dlq_and_is_committed()
    {
        var planId = Guid.NewGuid();
        var groupId = Guid.NewGuid().ToString();
        var json = CatalogEventHandlerTests.PlanEnvelope(planId, envelopeVersion: 99);
        await Produce(planId.ToString(), json);

        await using var running = await Run(groupId);
        var dead = ConsumeDlq(planId.ToString());

        Assert.Equal(json, dead.Message.Value);
        Assert.Equal(Topics.CatalogPlanEvents, Header(dead, "source-topic"));
        var partition = int.Parse(Header(dead, "source-partition"));
        var offset = long.Parse(Header(dead, "source-offset"));
        Assert.Contains("99", Header(dead, "error"));
        Assert.Equal(0, await CountVersions(planId));

        // The offset after the dead-lettered message is committed, so it is not delivered again.
        using var probe = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = kafka.BootstrapServers,
            GroupId = groupId
        }).Build();
        var partitionOffset = new TopicPartition(Topics.CatalogPlanEvents, partition);
        await Eventually(async () =>
        {
            await Task.CompletedTask;
            return probe.Committed([partitionOffset], TimeSpan.FromSeconds(5))[0].Offset.Value > offset;
        });
    }

    private async Task<int> CountVersions(Guid planId)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<RecommendationDbContext>()
            .PlanVersions.CountAsync(v => v.PlanId == planId);
    }

    private async Task Produce(string key, string value)
    {
        using var producer = new ProducerBuilder<string, string>(new ProducerConfig { BootstrapServers = kafka.BootstrapServers }).Build();
        await producer.ProduceAsync(Topics.CatalogPlanEvents, new Message<string, string> { Key = key, Value = value });
    }

    private async Task<RunningConsumer> Run(string? groupId = null)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Kafka:BootstrapServers"] = kafka.BootstrapServers,
            ["Kafka:GroupId"] = groupId ?? Guid.NewGuid().ToString()
        }).Build();

        var consumer = new CatalogConsumer(
            factory.Services.GetRequiredService<IServiceScopeFactory>(), config,
            factory.Services.GetRequiredService<CatalogEventHandler>(), TimeProvider.System,
            NullLogger<CatalogConsumer>.Instance);
        await consumer.StartAsync(CancellationToken.None);
        return new RunningConsumer(consumer);
    }

    private sealed class RunningConsumer(CatalogConsumer consumer) : IAsyncDisposable
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

    private static string Header(ConsumeResult<string, string> result, string name)
        => Encoding.UTF8.GetString(result.Message.Headers.GetLastBytes(name));

    private static async Task Eventually(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (!await condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("Condition was not met in time.");
            await Task.Delay(250);
        }
    }
}
