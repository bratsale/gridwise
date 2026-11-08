namespace GridWise.Batch.Domain;

/// <summary>The cheapest plan for one valid row of the file.</summary>
public class JobResult
{
    public Guid JobId { get; set; }
    public int Line { get; set; }
    public string CustomerId { get; set; } = null!;
    public Guid PlanId { get; set; }
    public int PlanVersionNo { get; set; }
    public string PlanName { get; set; } = null!;
    public decimal Total { get; set; }
    public int TaxGroupVersionNo { get; set; }
}
