namespace GridWise.Recommendation.Domain;

public class TaxGroupVersionCopy
{
    public string Code { get; set; } = null!;
    public int VersionNo { get; set; }
    public decimal Vat { get; set; }
    public decimal EcoTax { get; set; }
    public string Status { get; set; } = null!;
    public DateOnly EffectiveFrom { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
}
