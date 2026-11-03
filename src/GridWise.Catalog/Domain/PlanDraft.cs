namespace GridWise.Catalog.Domain;

public record PlanDraft(string Name, decimal Discount, DateOnly EffectiveFrom, IReadOnlyList<TierDraft> Tiers);
