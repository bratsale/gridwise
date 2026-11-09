using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using GridWise.Pricing;
using GridWise.Recommendation.Api;
using GridWise.Recommendation.Data;
using GridWise.Recommendation.Domain;
using GridWise.Recommendation.Events;
using GridWise.Recommendation.Telemetry;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GridWise.Recommendation.Services;

public abstract record CalculateResult
{
    public sealed record Created(RecommendationResponse Recommendation, CalculationTimings Timings) : CalculateResult;
    public sealed record ValidationFailed(Dictionary<string, string[]> Errors) : CalculateResult;
    public sealed record NoPlansAvailable : CalculateResult;
}

// Milliseconds spent in each phase of CalculateAsync (diagnostic, returned as Server-Timing).
public readonly record struct CalculationTimings(double DbRead, double Calc, double Json, double Save);

// One entry of the stored Items JSON. The breakdown is not stored: GET recalculates it from the used versions.
// Rows written before that change still have it, and it is used as is.
public record StoredQuote(
    Guid PlanId, int PlanVersionNo, string Name, decimal Total,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] BreakdownResponse? Breakdown = null);

public abstract record AcceptResult
{
    public sealed record Created(AcceptanceResponse Acceptance) : AcceptResult;
    public sealed record AlreadyAccepted(AcceptanceResponse Acceptance) : AcceptResult;
    public sealed record NotFound : AcceptResult;
    public sealed record ValidationFailed(Dictionary<string, string[]> Errors) : AcceptResult;
    public sealed record Conflict : AcceptResult;
}

public class RecommendationService(RecommendationDbContext db, PlanRanker ranker, TimeProvider time)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<CalculateResult> CalculateAsync(
        CreateRecommendationRequest req, string correlationId, CancellationToken ct)
    {
        if (decimal.Round(req.ConsumptionKwh, 3) != req.ConsumptionKwh)
            return new CalculateResult.ValidationFailed(new() { ["consumptionKwh"] = ["At most 3 decimal places."] });

        var now = NowMicroseconds();
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var kwh = req.ConsumptionKwh;

        var phaseStart = Stopwatch.GetTimestamp();
        double Lap()
        {
            var elapsed = Stopwatch.GetElapsedTime(phaseStart).TotalMilliseconds;
            phaseStart = Stopwatch.GetTimestamp();
            return elapsed;
        }
        double dbRead = 0, calc, json = 0, save;

        var taxGroup = await ranker.FindTaxGroupAsync(req.TaxGroup, today, ct);
        dbRead += Lap();

        if (taxGroup is null || taxGroup.Status == PlanRanker.Retired)
            return new CalculateResult.ValidationFailed(new() { ["taxGroup"] = ["Unknown or retired tax group."] });

        var active = await ranker.GetActivePlansAsync(today, ct);
        dbRead += Lap();

        if (active.Count == 0)
            return new CalculateResult.NoPlansAvailable();

        var items = PlanRanker.Rank(active, taxGroup, kwh)
            .Select(q => new PlanQuoteResponse(q.Version.PlanId, q.Version.VersionNo, q.Version.Name, q.Breakdown.Total, ToResponse(q.Breakdown)))
            .ToList();

        calc = Lap();

        var id = Guid.CreateVersion7();
        var best = items[0];

        var itemsJson = JsonSerializer.Serialize(
            items.Select(i => new StoredQuote(i.PlanId, i.PlanVersionNo, i.Name, i.Total)), Json);
        var payloadJson = JsonSerializer.Serialize(
            new RecommendationCalculatedV1(
                id, kwh, taxGroup.Code, taxGroup.VersionNo,
                best.PlanId, best.PlanVersionNo, best.Total, now),
            Json);
        var response = new RecommendationResponse(
            id, kwh, new TaxGroupUsed(taxGroup.Code, taxGroup.VersionNo, taxGroup.Vat, taxGroup.EcoTax), now, best, items);
        json = Lap();

        // Relaxed durability for this write only: a recommendation is derived data, and it is lost together
        // with its event in the same transaction, so the state stays consistent. See ADR 0007.
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await db.Database.ExecuteSqlRawAsync("SET LOCAL synchronous_commit = off", ct);

            db.PlanRecommendations.Add(new PlanRecommendation
            {
                Id = id,
                ConsumptionKwh = kwh,
                TaxGroupCode = taxGroup.Code,
                TaxGroupVersionNo = taxGroup.VersionNo,
                CalculatedAt = now,
                Items = itemsJson
            });

            db.OutboxMessages.Add(new OutboxMessage
            {
                Id = Guid.CreateVersion7(),
                AggregateType = "recommendation",
                AggregateId = id.ToString(),
                EventType = EventTypes.RecommendationCalculated,
                EventVersion = 1,
                Payload = payloadJson,
                CorrelationId = correlationId,
                TraceParent = Tracing.CurrentTraceParent(),
                OccurredAt = now
            });

            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        });
        save = Lap();

        return new CalculateResult.Created(response, new CalculationTimings(dbRead, calc, json, save));
    }

    public async Task<AcceptResult> AcceptAsync(
        Guid id, AcceptPlanRequest req, string correlationId, CancellationToken ct)
    {
        var row = await db.PlanRecommendations.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);
        if (row is null)
            return new AcceptResult.NotFound();

        var items = JsonSerializer.Deserialize<List<StoredQuote>>(row.Items, Json)!;
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
            TraceParent = Tracing.CurrentTraceParent(),
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

    private static AcceptResult AlreadyAccepted(Acceptance existing, Guid requestedPlanId, List<StoredQuote> items)
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
        var stored = JsonSerializer.Deserialize<List<StoredQuote>>(row.Items, Json)!;
        var items = await WithBreakdownsAsync(stored, taxGroup, row.ConsumptionKwh, ct);

        var acceptance = await db.Acceptances.AsNoTracking().FirstOrDefaultAsync(a => a.RecommendationId == id, ct);

        return new RecommendationResponse(
            row.Id, row.ConsumptionKwh,
            new TaxGroupUsed(taxGroup.Code, taxGroup.VersionNo, taxGroup.Vat, taxGroup.EcoTax),
            row.CalculatedAt, items.FirstOrDefault(), items,
            acceptance is null ? null : ToResponse(acceptance, stored));
    }

    // Ranking and totals come from the stored items; the breakdown is recalculated from the stored plan versions.
    private async Task<List<PlanQuoteResponse>> WithBreakdownsAsync(
        List<StoredQuote> stored, TaxGroupVersionCopy taxGroup, decimal kwh, CancellationToken ct)
    {
        var missing = stored.Where(i => i.Breakdown is null).ToList();
        var versions = new Dictionary<(Guid, int), PlanVersionCopy>();
        if (missing.Count > 0)
        {
            var planIds = missing.Select(i => i.PlanId).ToArray();
            var versionNos = missing.Select(i => i.PlanVersionNo).ToArray();
            var loaded = await db.PlanVersions
                .FromSqlInterpolated($"""
                    SELECT p.* FROM plan_versions p
                    JOIN unnest({planIds}::uuid[], {versionNos}::int[]) AS k(plan_id, version_no)
                      ON p.plan_id = k.plan_id AND p.version_no = k.version_no
                    """)
                .AsNoTracking()
                .ToListAsync(ct);
            versions = loaded.ToDictionary(v => (v.PlanId, v.VersionNo));
        }

        var group = new TaxGroup(taxGroup.Vat, taxGroup.EcoTax);
        return stored.Select(i =>
        {
            if (i.Breakdown is not null)
                return new PlanQuoteResponse(i.PlanId, i.PlanVersionNo, i.Name, i.Total, i.Breakdown);

            var v = versions[(i.PlanId, i.PlanVersionNo)];
            var breakdown = PriceCalculator.Calculate(new PricingPlan(v.Name, PlanRanker.ToTiers(v.Tiers), v.Discount), group, kwh);
            return new PlanQuoteResponse(i.PlanId, i.PlanVersionNo, i.Name, i.Total, ToResponse(breakdown));
        }).ToList();
    }

    // Stored timestamps have microsecond precision; use the same here so GET returns what POST returned.
    private DateTimeOffset NowMicroseconds()
    {
        var now = time.GetUtcNow();
        return new DateTimeOffset(now.Ticks - now.Ticks % 10, TimeSpan.Zero);
    }

    private static AcceptanceResponse ToResponse(Acceptance a, List<StoredQuote> items)
        => ToResponse(a.RecommendationId, items.Single(i => i.PlanId == a.PlanId), a.AcceptedAt);

    private static AcceptanceResponse ToResponse(Guid recommendationId, StoredQuote item, DateTimeOffset acceptedAt)
        => new(recommendationId, item.PlanId, item.PlanVersionNo, item.Total, acceptedAt);

    private static BreakdownResponse ToResponse(PriceBreakdown b)
        => new(
            b.Tiers.Select(t => new TierChargeResponse(t.Kwh, t.PricePerKwh, t.Amount)).ToList(),
            b.EnergySubtotal, b.DiscountAmount, b.NetEnergy, b.EcoTax, b.VatBase, b.VatAmount, b.Total);
}
