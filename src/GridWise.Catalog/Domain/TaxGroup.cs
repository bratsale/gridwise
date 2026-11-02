namespace GridWise.Catalog.Domain;

public class TaxGroup
{
    public string Code { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
    public List<TaxGroupVersion> Versions { get; set; } = [];
}
