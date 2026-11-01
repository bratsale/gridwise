namespace GridWise.Pricing;

public sealed record PlanQuote(PricingPlan Plan, PriceBreakdown Breakdown);

public static class Recommender
{
    public static IReadOnlyList<PlanQuote> Rank(IEnumerable<PricingPlan> plans, TaxGroup taxGroup, decimal kwh)
        => plans
            .Select(plan => new PlanQuote(plan, PriceCalculator.Calculate(plan, taxGroup, kwh)))
            .OrderBy(q => q.Breakdown.Total)
            .ThenBy(q => q.Plan.Name, StringComparer.Ordinal)
            .ToList();
}