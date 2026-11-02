namespace GridWise.Catalog.Domain;

public class PlanVersion
{
    public Guid PlanId { get; set; }
    public int VersionNo { get; set; }
    public string Name { get; set; } = null!;
    public decimal Discount { get; set; }
    public VersionStatus Status { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public List<PlanTier> Tiers { get; set; } = [];
}
