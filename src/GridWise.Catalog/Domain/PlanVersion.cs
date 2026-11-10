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
    /// <summary>Who made this version: the sub of the user's token (null for the seed data and versions from before this column).</summary>
    public string? ChangedBy { get; set; }
    public List<PlanTier> Tiers { get; set; } = [];
}
