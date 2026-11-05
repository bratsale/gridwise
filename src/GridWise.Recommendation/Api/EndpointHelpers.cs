namespace GridWise.Recommendation.Api;

public static class EndpointHelpers
{
    public static string ResolveCorrelationId(string? correlationId)
        => string.IsNullOrWhiteSpace(correlationId) ? Guid.NewGuid().ToString() : correlationId;
}
