namespace GridWise.Catalog.Events;

public record TaxGroupVersionCreatedV1(
    string Code,
    int VersionNo,
    decimal Vat,
    decimal EcoTax,
    string Status,
    DateOnly EffectiveFrom);
