namespace GridWise.Recommendation.Events;

public record PlanAcceptedV1(
    Guid RecommendationId,
    Guid PlanId,
    int PlanVersionNo,
    decimal Total,
    DateTimeOffset AcceptedAt);
