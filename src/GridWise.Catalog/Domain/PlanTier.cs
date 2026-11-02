namespace GridWise.Catalog.Domain;

public class PlanTier
{
    public Guid PlanId { get; set; }
    public int VersionNo { get; set; }
    public int TierNo { get; set; }
    public decimal? Threshold { get; set; }
    public decimal PricePerKwh { get; set; }
}
