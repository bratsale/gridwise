namespace GridWise.Catalog.Events;

public record TaxGroupVersionCreatedV1(
    string Code,
    int VersionNo,
    decimal Vat,
    decimal EcoTax,
    string Status,
    DateOnly EffectiveFrom,
    // Added later as an optional field (compatible, the version stays 1): the sub of the admin who made the version.
    // Null for versions written before it existed; consumers must not require it.
    string? ChangedBy = null);
