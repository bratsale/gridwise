using System.Text.Json;
using GridWise.Pricing;
using GridWise.Recommendation.Api;
using GridWise.Recommendation.Data;
using GridWise.Recommendation.Domain;
using GridWise.Recommendation.Events;
using Microsoft.EntityFrameworkCore;

namespace GridWise.Recommendation.Services;

public abstract record CalculateResult
{
    public sealed record Created(RecommendationResponse Recommendation) : CalculateResult;
    public sealed record ValidationFailed(Dictionary<string, string[]> Errors) : CalculateResult;
    public sealed record NoPlansAvailable : CalculateResult;
}

public class RecommendationService(RecommendationDbContext db, TimeProvider time)
{
    private const string Retired = "Retired";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<CalculateResult> CalculateAsync(
        CreateRecommendationRequest req, string correlationId, CancellationToken ct)
    {
        if (decimal.Round(req.ConsumptionKwh, 3) != req.ConsumptionKwh)
            return new CalculateResult.ValidationFailed(new() { ["consumptionKwh"] = ["At most 3 decimal places."] });

        // Stored timestamps have microsecond precision; use the same here so GET returns what POST returned.
        var now = time.GetUtcNow();
        now = new DateTimeOffset(now.Ticks - now.Ticks % 10, TimeSpan.Zero);
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var kwh = req.ConsumptionKwh;

        var taxGroup = await db.TaxGroupVersions.AsNoTracking()
            .Where(v => v.Code == req.TaxGroup && v.EffectiveFrom <= today)
            .OrderByDescending(v => v.EffectiveFrom).ThenByDescending(v => v.VersionNo)
            .FirstOrDefaultAsync(ct);

        if (taxGroup is null || taxGroup.Status == Retired)
            return new CalculateResult.ValidationFailed(new() { ["taxGroup"] = ["Unknown or retired tax group."] });

        // Pick the version valid today for every plan first, then drop the retired ones.
        var selected = await db.PlanVersions
            .FromSqlInterpolated($"""
                SELECT DISTINCT ON (plan_id) * FROM plan_versions
                WHERE effective_from <= {today}
                ORDER BY plan_id, effective_from DESC, version_no DESC
                """)
            .AsNoTracking()
            .ToListAsync(ct);

        var active = selected.Where(v => v.Status != Retired).ToList();
        if (active.Count == 0)
            return new CalculateResult.NoPlansAvailable();

        var byId = active.ToDictionary(v => $"{v.PlanId}:{v.VersionNo}");
        var plans = byId.Select(p => new PricingPlan(
            p.Value.Name, ToTiers(p.Value.Tiers), p.Value.Discount, p.Key)).ToList();

        var ranked = Recommender.Rank(plans, new TaxGroup(taxGroup.Vat, taxGroup.EcoTax), kwh);

        var items = ranked.Select(q =>
        {
            var version = byId[q.Plan.Id];
            return new PlanQuoteResponse(version.PlanId, version.VersionNo, q.Plan.Name, q.Breakdown.Total, ToResponse(q.Breakdown));
        }).ToList();

        var id = Guid.CreateVersion7();
        var best = items[0];

        db.PlanRecommendations.Add(new PlanRecommendation
        {
            Id = id,
            ConsumptionKwh = kwh,
            TaxGroupCode = taxGroup.Code,
            TaxGroupVersionNo = taxGroup.VersionNo,
            CalculatedAt = now,
            Items = JsonSerializer.Serialize(items, Json)
        });

        db.OutboxMessages.Add(new OutboxMessage
        {
            Id = Guid.CreateVersion7(),
            AggregateType = "recommendation",
            AggregateId = id.ToString(),
            EventType = EventTypes.RecommendationCalculated,
            EventVersion = 1,
            Payload = JsonSerializer.Serialize(
                new RecommendationCalculatedV1(
                    id, kwh, taxGroup.Code, taxGroup.VersionNo,
                    best.PlanId, best.PlanVersionNo, best.Total, now),
                Json),
            CorrelationId = correlationId,
            OccurredAt = now
        });

        await db.SaveChangesAsync(ct);

        return new CalculateResult.Created(new RecommendationResponse(
            id, kwh, new TaxGroupUsed(taxGroup.Code, taxGroup.VersionNo, taxGroup.Vat, taxGroup.EcoTax), now, best, items));
    }

    public async Task<RecommendationResponse?> GetAsync(Guid id, CancellationToken ct)
    {
        var row = await db.PlanRecommendations.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);
        if (row is null)
            return null;

        var taxGroup = await db.TaxGroupVersions.AsNoTracking()
            .SingleAsync(v => v.Code == row.TaxGroupCode && v.VersionNo == row.TaxGroupVersionNo, ct);
        var items = JsonSerializer.Deserialize<List<PlanQuoteResponse>>(row.Items, Json)!;

        return new RecommendationResponse(
            row.Id, row.ConsumptionKwh,
            new TaxGroupUsed(taxGroup.Code, taxGroup.VersionNo, taxGroup.Vat, taxGroup.EcoTax),
            row.CalculatedAt, items.FirstOrDefault(), items);
    }

    private static List<PricingTier> ToTiers(string json)
        => JsonSerializer.Deserialize<List<PlanTierV1>>(json, Json)!
            .Select(t => new PricingTier(t.Threshold, t.PricePerKwh)).ToList();

    private static BreakdownResponse ToResponse(PriceBreakdown b)
        => new(
            b.Tiers.Select(t => new TierChargeResponse(t.Kwh, t.PricePerKwh, t.Amount)).ToList(),
            b.EnergySubtotal, b.DiscountAmount, b.NetEnergy, b.EcoTax, b.VatBase, b.VatAmount, b.Total);
}
