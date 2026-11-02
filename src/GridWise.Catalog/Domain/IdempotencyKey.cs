namespace GridWise.Catalog.Domain;

public class IdempotencyKey
{
    public string Key { get; set; } = null!;
    public string Endpoint { get; set; } = null!;
    public string RequestHash { get; set; } = null!;
    public int ResponseStatus { get; set; }
    public string ResponseBody { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
}
