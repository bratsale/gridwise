using System.Net.Http.Json;
using System.Text.Json;
using Confluent.Kafka;
using GridWise.Recommendation.Api;
using GridWise.Recommendation.Data;
using GridWise.Recommendation.Events;
using GridWise.Recommendation.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GridWise.Recommendation.IntegrationTests;

[Collection("recommendation")]
public class OutboxPublisherTests(RecommendationFactory factory, KafkaFixture kafka) : IClassFixture<KafkaFixture>
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Recommendation_events_are_published_once_and_in_order()
    {
        var taxGroup = $"tg-{Guid.NewGuid():N}"[..20];
        var planId = Guid.NewGuid();
        const string tiers = """[{"threshold":null,"pricePerKwh":0.001}]""";
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RecommendationDbContext>();
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO tax_group_versions (code, version_no, vat, eco_tax, status, effective_from, received_at)
                VALUES ({taxGroup}, 1, 0.17, 0.005, 'Active', '2026-01-01', {DateTimeOffset.UtcNow})
                """);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO plan_versions (plan_id, version_no, name, discount, status, effective_from, tiers, received_at)
                VALUES ({planId}, 1, 'Flat', 0, 'Active', '2026-01-01', {tiers}::jsonb, {DateTimeOffset.UtcNow})
                """);
        }

        var calculatedCorrelation = Guid.NewGuid().ToString();
        var acceptedCorrelation = Guid.NewGuid().ToString();
        var client = factory.CreateClient();

        var calculate = new HttpRequestMessage(HttpMethod.Post, "/recommendations")
        {
            Content = JsonContent.Create(new { consumptionKwh = 250m, taxGroup })
        };
        calculate.Headers.Add("X-Correlation-Id", calculatedCorrelation);
        var created = await client.SendAsync(calculate);
        created.EnsureSuccessStatusCode();
        var recommendation = (await created.Content.ReadFromJsonAsync<RecommendationResponse>())!;

        var accept = new HttpRequestMessage(HttpMethod.Post, $"/recommendations/{recommendation.Id}/accept")
        {
            Content = JsonContent.Create(new { planId = recommendation.Recommended!.PlanId })
        };
        accept.Headers.Add("X-Correlation-Id", acceptedCorrelation);
        (await client.SendAsync(accept)).EnsureSuccessStatusCode();

        using var producer = new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = kafka.BootstrapServers,
            Acks = Acks.All,
            EnableIdempotence = true
        }).Build();

        // Rows left by other tests in the shared database may come first.
        while (await Publish(producer) > 0) { }
        Assert.Equal(0, await Publish(producer));

        var messages = ConsumeAll(recommendation.Id.ToString());
        Assert.Equal(2, messages.Count);

        var calculated = JsonSerializer.Deserialize<EventEnvelope>(messages[0].Message.Value, Web)!;
        Assert.Equal(EventTypes.RecommendationCalculated, calculated.Type);
        Assert.Equal(1, calculated.Version);
        Assert.Equal(calculatedCorrelation, calculated.CorrelationId);
        Assert.Equal(recommendation.Id, calculated.Data.GetProperty("recommendationId").GetGuid());

        var accepted = JsonSerializer.Deserialize<EventEnvelope>(messages[1].Message.Value, Web)!;
        Assert.Equal(EventTypes.PlanAccepted, accepted.Type);
        Assert.Equal(1, accepted.Version);
        Assert.Equal(acceptedCorrelation, accepted.CorrelationId);
        Assert.Equal(recommendation.Recommended.PlanId, accepted.Data.GetProperty("planId").GetGuid());

        using var scope2 = factory.Services.CreateScope();
        var rows = await scope2.ServiceProvider.GetRequiredService<RecommendationDbContext>()
            .OutboxMessages.AsNoTracking().Where(m => m.AggregateId == recommendation.Id.ToString()).ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.NotNull(r.PublishedAt));
        Assert.Equal([calculated.Id, accepted.Id], rows.OrderBy(r => r.Id).Select(r => r.Id));
    }

    private async Task<int> Publish(IProducer<string, string> producer)
    {
        using var scope = factory.Services.CreateScope();
        return await OutboxPublisher.PublishPendingAsync(
            scope.ServiceProvider.GetRequiredService<RecommendationDbContext>(),
            producer, TimeProvider.System, 100, CancellationToken.None);
    }

    // Reads the topic from the start; stops once the key was seen and the topic has been quiet for 3 s.
    private List<ConsumeResult<string, string>> ConsumeAll(string key)
    {
        using var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = kafka.BootstrapServers,
            GroupId = Guid.NewGuid().ToString(),
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false
        }).Build();
        consumer.Subscribe(Topics.RecommendationEvents);

        var found = new List<ConsumeResult<string, string>>();
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        var lastMessage = DateTime.UtcNow;
        while (DateTime.UtcNow < deadline && (found.Count == 0 || DateTime.UtcNow - lastMessage < TimeSpan.FromSeconds(3)))
        {
            var result = consumer.Consume(TimeSpan.FromMilliseconds(500));
            if (result is null)
                continue;
            lastMessage = DateTime.UtcNow;
            if (result.Message.Key == key)
                found.Add(result);
        }
        return found;
    }
}
