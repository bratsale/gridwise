namespace GridWise.Recommendation.Events;

public static class EventTypes
{
    public const string PlanVersionCreated = "PlanVersionCreated";
    public const string TaxGroupVersionCreated = "TaxGroupVersionCreated";
    public const string RecommendationCalculated = "RecommendationCalculated";
    public const string PlanAccepted = "PlanAccepted";
    public const string BatchChunkRequested = "BatchChunkRequested";
    public const string BatchChunkCompleted = "BatchChunkCompleted";
}

public static class Topics
{
    public const string CatalogPlanEvents = "catalog.plan-events";
    public const string CatalogTaxGroupEvents = "catalog.tax-group-events";
    public const string RecommendationEvents = "recommendation.events";
    public const string RecommendationDlq = "recommendation.dlq";
    public const string BatchChunks = "batch.chunks";
    public const string BatchResults = "batch.results";
}
