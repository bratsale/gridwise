namespace GridWise.Recommendation.Events;

public record PlanTierV1(decimal? Threshold, decimal PricePerKwh);

public record PlanVersionCreatedV1(
    Guid Id,
    int VersionNo,
    string Name,
    decimal Discount,
    string Status,
    DateOnly EffectiveFrom,
    IReadOnlyList<PlanTierV1> Tiers);
