using System.Text.Json;
using GridWise.Pricing;
using GridWise.Recommendation.Data;
using GridWise.Recommendation.Domain;
using GridWise.Recommendation.Events;
using Microsoft.EntityFrameworkCore;

namespace GridWise.Recommendation.Services;

public record RankedPlan(PlanVersionCopy Version, PriceBreakdown Breakdown);

/// <summary>Loads the tax group and the plan versions valid on a day from the local copy and ranks them with Pricing. Used by the API and the batch worker.</summary>
public class PlanRanker(RecommendationDbContext db)
{
    public const string Retired = "Retired";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>The version of the tax group valid on the day. It may be Retired; null if the code is unknown.</summary>
    public Task<TaxGroupVersionCopy?> FindTaxGroupAsync(string code, DateOnly today, CancellationToken ct)
        => db.TaxGroupVersions.AsNoTracking()
            .Where(v => v.Code == code && v.EffectiveFrom <= today)
            .OrderByDescending(v => v.EffectiveFrom).ThenByDescending(v => v.VersionNo)
            .FirstOrDefaultAsync(ct);

    /// <summary>The version valid on the day of every plan whose version is not Retired.</summary>
    public async Task<List<PlanVersionCopy>> GetActivePlansAsync(DateOnly today, CancellationToken ct)
    {
        // Pick the version valid today for every plan first, then drop the retired ones.
        var selected = await db.PlanVersions
            .FromSqlInterpolated($"""
                SELECT DISTINCT ON (plan_id) * FROM plan_versions
                WHERE effective_from <= {today}
                ORDER BY plan_id, effective_from DESC, version_no DESC
                """)
            .AsNoTracking()
            .ToListAsync(ct);

        return selected.Where(v => v.Status != Retired).ToList();
    }

    /// <summary>The plans priced for the consumption, cheapest first.</summary>
    public static List<RankedPlan> Rank(IEnumerable<PlanVersionCopy> plans, TaxGroupVersionCopy taxGroup, decimal kwh)
    {
        var byId = plans.ToDictionary(v => $"{v.PlanId}:{v.VersionNo}");
        var pricingPlans = byId.Select(p => new PricingPlan(p.Value.Name, ToTiers(p.Value.Tiers), p.Value.Discount, p.Key));

        return Recommender.Rank(pricingPlans, new TaxGroup(taxGroup.Vat, taxGroup.EcoTax), kwh)
            .Select(q => new RankedPlan(byId[q.Plan.Id], q.Breakdown))
            .ToList();
    }

    public static List<PricingTier> ToTiers(string json)
        => JsonSerializer.Deserialize<List<PlanTierV1>>(json, Json)!
            .Select(t => new PricingTier(t.Threshold, t.PricePerKwh)).ToList();
}
