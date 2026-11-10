namespace GridWise.Catalog.Events;

public record PlanTierV1(decimal? Threshold, decimal PricePerKwh);

public record PlanVersionCreatedV1(
    Guid Id,
    int VersionNo,
    string Name,
    decimal Discount,
    string Status,
    DateOnly EffectiveFrom,
    IReadOnlyList<PlanTierV1> Tiers,
    // Added later as an optional field (compatible, the version stays 1): the sub of the admin who made the version.
    // Null for versions written before it existed; consumers must not require it.
    string? ChangedBy = null);
