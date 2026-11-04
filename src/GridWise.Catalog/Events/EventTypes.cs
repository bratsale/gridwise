namespace GridWise.Catalog.Events;

public static class EventTypes
{
    public const string PlanVersionCreated = "PlanVersionCreated";
    public const string TaxGroupVersionCreated = "TaxGroupVersionCreated";
}

public static class Topics
{
    public const string CatalogPlanEvents = "catalog.plan-events";
    public const string CatalogTaxGroupEvents = "catalog.tax-group-events";
}
