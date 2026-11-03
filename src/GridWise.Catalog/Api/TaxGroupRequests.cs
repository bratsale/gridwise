using System.ComponentModel.DataAnnotations;

namespace GridWise.Catalog.Api;

public record CreateTaxGroupRequest(
    [Required, StringLength(50), RegularExpression("^[a-z][a-z0-9-]*$")] string Code,
    [Range(0, 1)] decimal Vat,
    [Range(0, double.MaxValue)] decimal EcoTax,
    DateOnly? EffectiveFrom);

public record TaxGroupVersionRequest(
    [Range(0, 1)] decimal Vat,
    [Range(0, double.MaxValue)] decimal EcoTax,
    DateOnly? EffectiveFrom);

public record TaxGroupResponse(
    string Code,
    int VersionNo,
    decimal Vat,
    decimal EcoTax,
    string Status,
    DateOnly EffectiveFrom);
