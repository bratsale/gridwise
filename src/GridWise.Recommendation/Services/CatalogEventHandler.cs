using System.Text.Json;
using GridWise.Recommendation.Data;
using GridWise.Recommendation.Events;
using Microsoft.EntityFrameworkCore;

namespace GridWise.Recommendation.Services;

public enum HandleOutcome
{
    Applied,
    Duplicate,
    SkippedUnknownType
}

public class PoisonMessageException(string message, Exception? inner = null) : Exception(message, inner);

public class CatalogEventHandler(TimeProvider time)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<HandleOutcome> HandleAsync(RecommendationDbContext db, string json, CancellationToken ct)
    {
        EventEnvelope envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<EventEnvelope>(json, Json)
                ?? throw new PoisonMessageException("Message is not an event envelope.");
        }
        catch (JsonException ex)
        {
            throw new PoisonMessageException("Message is not valid JSON.", ex);
        }

        return envelope.Type switch
        {
            EventTypes.PlanVersionCreated => await ApplyPlanVersionAsync(db, envelope, ct),
            EventTypes.TaxGroupVersionCreated => await ApplyTaxGroupVersionAsync(db, envelope, ct),
            _ => HandleOutcome.SkippedUnknownType
        };
    }

    private async Task<HandleOutcome> ApplyPlanVersionAsync(RecommendationDbContext db, EventEnvelope envelope, CancellationToken ct)
    {
        var e = ReadData<PlanVersionCreatedV1>(envelope);
        var tiers = envelope.Data.GetProperty("tiers").GetRawText();
        var receivedAt = time.GetUtcNow();

        var rows = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO plan_versions (plan_id, version_no, name, discount, status, effective_from, tiers, received_at)
            VALUES ({e.Id}, {e.VersionNo}, {e.Name}, {e.Discount}, {e.Status}, {e.EffectiveFrom}, {tiers}::jsonb, {receivedAt})
            ON CONFLICT (plan_id, version_no) DO NOTHING
            """, ct);

        return rows > 0 ? HandleOutcome.Applied : HandleOutcome.Duplicate;
    }

    private async Task<HandleOutcome> ApplyTaxGroupVersionAsync(RecommendationDbContext db, EventEnvelope envelope, CancellationToken ct)
    {
        var e = ReadData<TaxGroupVersionCreatedV1>(envelope);
        var receivedAt = time.GetUtcNow();

        var rows = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO tax_group_versions (code, version_no, vat, eco_tax, status, effective_from, received_at)
            VALUES ({e.Code}, {e.VersionNo}, {e.Vat}, {e.EcoTax}, {e.Status}, {e.EffectiveFrom}, {receivedAt})
            ON CONFLICT (code, version_no) DO NOTHING
            """, ct);

        return rows > 0 ? HandleOutcome.Applied : HandleOutcome.Duplicate;
    }

    private static T ReadData<T>(EventEnvelope envelope) where T : class
    {
        if (envelope.Version != 1)
            throw new PoisonMessageException($"Unsupported version {envelope.Version} of {envelope.Type}.");

        try
        {
            return envelope.Data.Deserialize<T>(Json)
                ?? throw new PoisonMessageException($"{envelope.Type} has no data.");
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            throw new PoisonMessageException($"{envelope.Type} data cannot be read.", ex);
        }
    }
}
