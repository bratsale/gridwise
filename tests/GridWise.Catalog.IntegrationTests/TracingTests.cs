using System.Net.Http.Json;
using System.Text;
using Confluent.Kafka;
using GridWise.Catalog.Api;
using GridWise.Catalog.Data;
using GridWise.Catalog.Events;
using GridWise.Catalog.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GridWise.Catalog.IntegrationTests;

[Collection("catalog")]
public class TracingTests(CatalogFactory factory, KafkaFixture kafka) : IClassFixture<KafkaFixture>
{
    // Creates a plan as a request that belongs to a trace started elsewhere (it carries a traceparent header).
    private async Task<Guid> CreatePlan(string traceParent)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/plans")
        {
            Content = JsonContent.Create(new
            {
                name = "Traced",
                discount = 0.05m,
                tiers = new[] { new { threshold = (decimal?)null, pricePerKwh = 0.06m } }
            })
        };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        request.Headers.Add("traceparent", traceParent);
        var response = await factory.CreateClient().SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<PlanResponse>())!.Id;
    }

    [Fact]
    public async Task Outbox_row_gets_the_traceparent_of_the_request()
    {
        using var traces = new TraceCapture();

        var planId = await CreatePlan(traces.TraceParent);

        using var scope = factory.Services.CreateScope();
        var row = await scope.ServiceProvider.GetRequiredService<CatalogDbContext>()
            .OutboxMessages.AsNoTracking().SingleAsync(m => m.AggregateId == planId.ToString());
        Assert.NotNull(row.TraceParent);
        Assert.StartsWith($"00-{traces.TraceId}-", row.TraceParent);   // the same trace ...
        Assert.NotEqual(traces.TraceParent, row.TraceParent);           // ... a span of this service inside it
    }

    [Fact]
    public async Task Publisher_starts_a_publish_span_under_the_stored_context_and_sends_it_as_a_header()
    {
        using var traces = new TraceCapture();
        var planId = await CreatePlan(traces.TraceParent);

        string storedTraceParent;
        using (var scope = factory.Services.CreateScope())
            storedTraceParent = (await scope.ServiceProvider.GetRequiredService<CatalogDbContext>()
                .OutboxMessages.AsNoTracking().SingleAsync(m => m.AggregateId == planId.ToString())).TraceParent!;

        using var producer = new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = kafka.BootstrapServers,
            Acks = Acks.All,
            EnableIdempotence = true
        }).Build();
        while (true) // other tests may have left unpublished rows ahead of ours
        {
            using var scope = factory.Services.CreateScope();
            if (await OutboxPublisher.PublishPendingAsync(
                    scope.ServiceProvider.GetRequiredService<CatalogDbContext>(), producer, TimeProvider.System, 100, CancellationToken.None) == 0)
                break;
        }

        var publish = traces.Stopped.Single(a => a.OperationName == "publish" && a.ParentId == storedTraceParent);
        Assert.Equal(traces.TraceId, publish.TraceId.ToHexString());

        var message = Consume(planId.ToString());
        var header = Encoding.UTF8.GetString(message.Message.Headers.GetLastBytes("traceparent"));
        Assert.Equal(publish.Id, header);
    }

    private ConsumeResult<string, string> Consume(string key)
    {
        using var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = kafka.BootstrapServers,
            GroupId = Guid.NewGuid().ToString(),
            AutoOffsetReset = AutoOffsetReset.Earliest
        }).Build();
        consumer.Subscribe(Topics.CatalogPlanEvents);

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            var result = consumer.Consume(TimeSpan.FromMilliseconds(500));
            if (result?.Message.Key == key)
                return result;
        }
        throw new TimeoutException($"No message with key {key} arrived.");
    }
}
