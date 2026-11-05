namespace GridWise.Recommendation.Events;

public record RecommendationCalculatedV1(
    Guid RecommendationId,
    decimal ConsumptionKwh,
    string TaxGroupCode,
    int TaxGroupVersionNo,
    Guid? RecommendedPlanId,
    int? RecommendedPlanVersionNo,
    decimal? RecommendedTotal,
    DateTimeOffset CalculatedAt);
