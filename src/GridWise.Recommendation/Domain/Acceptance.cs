namespace GridWise.Recommendation.Domain;

public class Acceptance
{
    public Guid RecommendationId { get; set; }
    public Guid PlanId { get; set; }
    public int PlanVersionNo { get; set; }
    public DateTimeOffset AcceptedAt { get; set; }
}
