namespace GridWise.Catalog.Domain;

public class TaxGroupVersion
{
    public string Code { get; set; } = null!;
    public int VersionNo { get; set; }
    public decimal Vat { get; set; }
    public decimal EcoTax { get; set; }
    public VersionStatus Status { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    /// <summary>Who made this version: the sub of the user's token (null for the seed data and versions from before this column).</summary>
    public string? ChangedBy { get; set; }
}
