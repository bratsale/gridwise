using System.Globalization;
using System.Text.Json;
using GridWise.Recommendation.Data;
using GridWise.Recommendation.Domain;
using GridWise.Recommendation.Events;
using GridWise.Recommendation.Telemetry;
using Microsoft.EntityFrameworkCore;

namespace GridWise.Recommendation.Services;

public enum ChunkOutcome
{
    Processed,
    Duplicate,
    SkippedUnknownType
}

/// <summary>Prices the rows of one chunk of a batch job and writes the result to the outbox, all in one transaction.</summary>
public class BatchChunkProcessor(RecommendationDbContext db, PlanRanker ranker, TimeProvider time)
{
    private const int MaxCustomerIdLength = 100;
    private const int MaxDecimals = 3;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<ChunkOutcome> HandleAsync(string json, CancellationToken ct)
    {
        var envelope = EventReader.Parse(json);
        if (envelope.Type != EventTypes.BatchChunkRequested)
            return ChunkOutcome.SkippedUnknownType;

        var chunk = EventReader.ReadData<BatchChunkRequestedV1>(envelope);
        if (chunk.Rows is null)
            throw new PoisonMessageException($"{envelope.Type} has no rows.");

        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(ct);

            var now = time.GetUtcNow();
            var inserted = await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO processed_chunks (job_id, chunk_no, processed_at)
                VALUES ({chunk.JobId}, {chunk.ChunkNo}, {now})
                ON CONFLICT DO NOTHING
                """, ct);
            if (inserted == 0)
                return ChunkOutcome.Duplicate; // already processed; its result is in the outbox

            var today = DateOnly.FromDateTime(now.UtcDateTime);
            var plans = await ranker.GetActivePlansAsync(today, ct);
            if (plans.Count == 0)
                throw new TemporarilyUnavailableException("There are no active plans yet.");

            var taxGroups = new Dictionary<string, TaxGroupVersionCopy?>();
            var results = new List<RowResultV1>();
            var errors = new List<RowErrorV1>();
            foreach (var row in chunk.Rows)
            {
                var error = TryParse(row.Text, out var customerId, out var code, out var kwh);
                TaxGroupVersionCopy? taxGroup = null;
                if (error is null)
                {
                    if (!taxGroups.TryGetValue(code, out taxGroup))
                        taxGroups[code] = taxGroup = await ranker.FindTaxGroupAsync(code, today, ct);

                    error = taxGroup is null ? $"Unknown tax group '{code}'."
                        : taxGroup.Status == PlanRanker.Retired ? $"Tax group '{code}' is retired."
                        : null;
                }

                if (error is not null)
                {
                    errors.Add(new RowErrorV1(row.Line, error));
                    continue;
                }

                var best = PlanRanker.Rank(plans, taxGroup!, kwh)[0];
                results.Add(new RowResultV1(
                    row.Line, customerId, best.Version.PlanId, best.Version.VersionNo, best.Version.Name,
                    best.Breakdown.Total, taxGroup!.VersionNo));
            }

            db.OutboxMessages.Add(new OutboxMessage
            {
                Id = Guid.CreateVersion7(),
                AggregateType = "batch-chunk-result",
                AggregateId = chunk.JobId.ToString(),
                EventType = EventTypes.BatchChunkCompleted,
                EventVersion = 1,
                Payload = JsonSerializer.Serialize(
                    new BatchChunkCompletedV1(chunk.JobId, chunk.ChunkNo, results, errors), Json),
                TraceParent = Tracing.CurrentTraceParent(),
                OccurredAt = now
            });
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            return ChunkOutcome.Processed;
        });
    }

    // Exactly 3 comma-separated fields, no quoting. Returns the error message, or null if the row is fine.
    private static string? TryParse(string text, out string customerId, out string taxGroup, out decimal kwh)
    {
        customerId = taxGroup = "";
        kwh = 0;

        var fields = text.Split(',');
        if (fields.Length != 3)
            return $"Expected 3 comma-separated fields, found {fields.Length}.";

        customerId = fields[0].Trim();
        var consumption = fields[1].Trim();
        taxGroup = fields[2].Trim();

        if (customerId.Length == 0)
            return "customer_id is empty.";
        if (customerId.Length > MaxCustomerIdLength)
            return $"customer_id is longer than {MaxCustomerIdLength} characters.";

        if (!decimal.TryParse(consumption, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out kwh))
            return $"consumption_kwh '{consumption}' is not a number.";
        if (kwh < 0)
            return "consumption_kwh must not be negative.";
        if (decimal.Round(kwh, MaxDecimals) != kwh)
            return $"consumption_kwh has more than {MaxDecimals} decimal places.";

        if (taxGroup.Length == 0)
            return "tax_group is empty.";

        return null;
    }
}
