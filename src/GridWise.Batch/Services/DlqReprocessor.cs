using System.Text;
using Confluent.Kafka;
using GridWise.Batch.Data;
using GridWise.Batch.Events;
using GridWise.Batch.Telemetry;

namespace GridWise.Batch.Services;

public record DlqResult(int Reprocessed, int FailedAgain);

// One-off command: handles every message that is in batch.dlq when it starts.
// Messages that fail again are appended to the end of the same topic, so they are not handled twice in one run.
public static class DlqReprocessor
{
    public const string GroupId = "batch-dlq-reprocess";

    public static async Task<DlqResult> RunAsync(
        Func<BatchDbContext> createDb, ConsumerConfig consumerConfig,
        IProducer<string, string> producer, ILogger logger, CancellationToken ct)
    {
        consumerConfig.GroupId = GroupId;
        consumerConfig.EnableAutoCommit = false;
        consumerConfig.AutoOffsetReset = AutoOffsetReset.Earliest;

        var reprocessed = 0;
        var failedAgain = 0;

        using var consumer = new ConsumerBuilder<string, string>(consumerConfig).Build();

        // High watermarks at start: the end of the topic that this run has to reach.
        var endOffsets = new Dictionary<TopicPartition, long>();
        using (var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = consumerConfig.BootstrapServers }).Build())
        {
            var topic = admin.GetMetadata(Topics.BatchDlq, TimeSpan.FromSeconds(10)).Topics.Single();
            foreach (var partition in topic.Partitions)
            {
                var tp = new TopicPartition(Topics.BatchDlq, partition.PartitionId);
                endOffsets[tp] = consumer.QueryWatermarkOffsets(tp, TimeSpan.FromSeconds(10)).High.Value;
            }
        }

        consumer.Subscribe(Topics.BatchDlq);

        try
        {
            var pending = endOffsets.Where(e => e.Value > 0).Select(e => e.Key).ToHashSet();
            while (pending.Count > 0)
            {
                var message = consumer.Consume(TimeSpan.FromSeconds(1));
                if (message is not null)
                {
                    if (message.Offset.Value >= endOffsets[message.TopicPartition])
                    {
                        // Added by this run: not ours to handle. Leave it uncommitted.
                    }
                    else
                    {
                        // A child of the original trace (its traceparent header was kept in the dead-letter message).
                        using var activity = Tracing.StartProcess(message);
                        try
                        {
                            using var db = createDb();
                            var outcome = await HandleAsync(db, SourceTopic(message), message.Message.Value, ct);
                            logger.LogInformation("DLQ message {Partition}@{Offset}: {Outcome}",
                                message.Partition.Value, message.Offset.Value, outcome);
                            reprocessed++;
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            logger.LogWarning(ex, "DLQ message {Partition}@{Offset} failed again.",
                                message.Partition.Value, message.Offset.Value);
                            await producer.ProduceAsync(Topics.BatchDlq, Requeue(message, ex), ct);
                            failedAgain++;
                        }

                        consumer.Commit(message);
                    }
                }

                foreach (var tp in pending.ToList())
                {
                    if (consumer.Assignment.Contains(tp) && consumer.Position(tp).Value >= endOffsets[tp])
                        pending.Remove(tp);
                }
            }
        }
        finally
        {
            consumer.Close();
        }

        return new DlqResult(reprocessed, failedAgain);
    }

    // The message goes back to the handler of the topic it came from: the same code as the live consumer.
    private static async Task<string> HandleAsync(BatchDbContext db, string? sourceTopic, string value, CancellationToken ct)
    {
        if (sourceTopic == Topics.BatchResults)
            return (await new BatchResultHandler(db, TimeProvider.System).HandleAsync(value, ct)).ToString();

        // Never skipped silently: it fails again and goes back to the end of the topic.
        throw new InvalidOperationException(
            $"Cannot reprocess a message from {(sourceTopic is null ? "an unknown source (no source-topic header)" : $"topic '{sourceTopic}'")}.");
    }

    private static string? SourceTopic(ConsumeResult<string, string> message)
        => message.Message.Headers is { } headers && headers.TryGetLastBytes("source-topic", out var bytes)
            ? Encoding.UTF8.GetString(bytes)
            : null;

    private static Message<string, string> Requeue(ConsumeResult<string, string> message, Exception error)
    {
        var headers = message.Message.Headers ?? [];
        var attempt = headers.TryGetLastBytes("attempt", out var bytes) && int.TryParse(Encoding.UTF8.GetString(bytes), out var n) ? n : 0;

        var requeued = new Headers();
        foreach (var header in headers.Where(h => h.Key is not ("attempt" or "error")))
            requeued.Add(header.Key, header.GetValueBytes());
        requeued.Add("attempt", Encoding.UTF8.GetBytes((attempt + 1).ToString()));
        requeued.Add("error", Encoding.UTF8.GetBytes(error.Message));

        return new Message<string, string> { Key = message.Message.Key, Value = message.Message.Value, Headers = requeued };
    }
}
