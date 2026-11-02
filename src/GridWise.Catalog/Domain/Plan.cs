namespace GridWise.Catalog.Domain;

public class Plan
{
    public Guid Id { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public List<PlanVersion> Versions { get; set; } = [];
}
