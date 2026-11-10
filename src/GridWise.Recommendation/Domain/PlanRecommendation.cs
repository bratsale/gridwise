namespace GridWise.Recommendation.Domain;

public class PlanRecommendation
{
    public Guid Id { get; set; }
    public decimal ConsumptionKwh { get; set; }
    public string TaxGroupCode { get; set; } = null!;
    public int TaxGroupVersionNo { get; set; }
    public DateTimeOffset CalculatedAt { get; set; }
    public string Items { get; set; } = null!;
    /// <summary>The sub of the user who calculated it; a customer may read and accept only their own.</summary>
    public string? CreatedBy { get; set; }
}
