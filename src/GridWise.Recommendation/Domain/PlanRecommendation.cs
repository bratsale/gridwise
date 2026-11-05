namespace GridWise.Recommendation.Domain;

public class PlanRecommendation
{
    public Guid Id { get; set; }
    public decimal ConsumptionKwh { get; set; }
    public string TaxGroupCode { get; set; } = null!;
    public int TaxGroupVersionNo { get; set; }
    public DateTimeOffset CalculatedAt { get; set; }
    public string Items { get; set; } = null!;
}
