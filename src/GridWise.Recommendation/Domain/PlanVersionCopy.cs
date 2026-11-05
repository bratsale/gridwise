namespace GridWise.Recommendation.Domain;

public class PlanVersionCopy
{
    public Guid PlanId { get; set; }
    public int VersionNo { get; set; }
    public string Name { get; set; } = null!;
    public decimal Discount { get; set; }
    public string Status { get; set; } = null!;
    public DateOnly EffectiveFrom { get; set; }
    public string Tiers { get; set; } = null!;
    public DateTimeOffset ReceivedAt { get; set; }
}
