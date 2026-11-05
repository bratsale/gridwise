using System.Text.Json;
using GridWise.Pricing;
using GridWise.Recommendation.Api;
using GridWise.Recommendation.Data;
using GridWise.Recommendation.Domain;
using GridWise.Recommendation.Events;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GridWise.Recommendation.Services;

public abstract record CalculateResult
{
    public sealed record Created(RecommendationResponse Recommendation) : CalculateResult;
    public sealed record ValidationFailed(Dictionary<string, string[]> Errors) : CalculateResult;
    public sealed record NoPlansAvailable : CalculateResult;
}

public abstract record AcceptResult
{
    public sealed record Created(AcceptanceResponse Acceptance) : AcceptResult;
    public sealed record AlreadyAccepted(AcceptanceResponse Acceptance) : AcceptResult;
    public sealed record NotFound : AcceptResult;
    public sealed record ValidationFailed(Dictionary<string, string[]> Errors) : AcceptResult;
    public sealed record Conflict : AcceptResult;
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

        var now = NowMicroseconds();
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

    public async Task<AcceptResult> AcceptAsync(
        Guid id, AcceptPlanRequest req, string correlationId, CancellationToken ct)
    {
        var row = await db.PlanRecommendations.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);
        if (row is null)
            return new AcceptResult.NotFound();

        var items = JsonSerializer.Deserialize<List<PlanQuoteResponse>>(row.Items, Json)!;
        var item = items.FirstOrDefault(i => i.PlanId == req.PlanId);
        if (item is null)
            return new AcceptResult.ValidationFailed(new() { ["planId"] = ["The plan is not part of this recommendation."] });

        var existing = await db.Acceptances.AsNoTracking().FirstOrDefaultAsync(a => a.RecommendationId == id, ct);
        if (existing is not null)
            return AlreadyAccepted(existing, req.PlanId, items);

        var now = NowMicroseconds();

        db.Acceptances.Add(new Acceptance
        {
            RecommendationId = id,
            PlanId = item.PlanId,
            PlanVersionNo = item.PlanVersionNo,
            AcceptedAt = now
        });

        db.OutboxMessages.Add(new OutboxMessage
        {
            Id = Guid.CreateVersion7(),
            AggregateType = "recommendation",
            AggregateId = id.ToString(),
            EventType = EventTypes.PlanAccepted,
            EventVersion = 1,
            Payload = JsonSerializer.Serialize(
                new PlanAcceptedV1(id, item.PlanId, item.PlanVersionNo, item.Total, now), Json),
            CorrelationId = correlationId,
            OccurredAt = now
        });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            TableName: "acceptances"
        })
        {
            db.ChangeTracker.Clear();
            return AlreadyAccepted(
                await db.Acceptances.AsNoTracking().FirstAsync(a => a.RecommendationId == id, ct), req.PlanId, items);
        }

        return new AcceptResult.Created(ToResponse(id, item, now));
    }

    private static AcceptResult AlreadyAccepted(Acceptance existing, Guid requestedPlanId, List<PlanQuoteResponse> items)
        => existing.PlanId == requestedPlanId
            ? new AcceptResult.AlreadyAccepted(ToResponse(existing, items))
            : new AcceptResult.Conflict();

    public async Task<RecommendationResponse?> GetAsync(Guid id, CancellationToken ct)
    {
        var row = await db.PlanRecommendations.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);
        if (row is null)
            return null;

        var taxGroup = await db.TaxGroupVersions.AsNoTracking()
            .SingleAsync(v => v.Code == row.TaxGroupCode && v.VersionNo == row.TaxGroupVersionNo, ct);
        var items = JsonSerializer.Deserialize<List<PlanQuoteResponse>>(row.Items, Json)!;

        var acceptance = await db.Acceptances.AsNoTracking().FirstOrDefaultAsync(a => a.RecommendationId == id, ct);

        return new RecommendationResponse(
            row.Id, row.ConsumptionKwh,
            new TaxGroupUsed(taxGroup.Code, taxGroup.VersionNo, taxGroup.Vat, taxGroup.EcoTax),
            row.CalculatedAt, items.FirstOrDefault(), items,
            acceptance is null ? null : ToResponse(acceptance, items));
    }

    // Stored timestamps have microsecond precision; use the same here so GET returns what POST returned.
    private DateTimeOffset NowMicroseconds()
    {
        var now = time.GetUtcNow();
        return new DateTimeOffset(now.Ticks - now.Ticks % 10, TimeSpan.Zero);
    }

    private static AcceptanceResponse ToResponse(Acceptance a, List<PlanQuoteResponse> items)
        => ToResponse(a.RecommendationId, items.Single(i => i.PlanId == a.PlanId), a.AcceptedAt);

    private static AcceptanceResponse ToResponse(Guid recommendationId, PlanQuoteResponse item, DateTimeOffset acceptedAt)
        => new(recommendationId, item.PlanId, item.PlanVersionNo, item.Total, acceptedAt);

    private static List<PricingTier> ToTiers(string json)
        => JsonSerializer.Deserialize<List<PlanTierV1>>(json, Json)!
            .Select(t => new PricingTier(t.Threshold, t.PricePerKwh)).ToList();

    private static BreakdownResponse ToResponse(PriceBreakdown b)
        => new(
            b.Tiers.Select(t => new TierChargeResponse(t.Kwh, t.PricePerKwh, t.Amount)).ToList(),
            b.EnergySubtotal, b.DiscountAmount, b.NetEnergy, b.EcoTax, b.VatBase, b.VatAmount, b.Total);
}
