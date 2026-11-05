using System.ComponentModel.DataAnnotations;

namespace GridWise.Recommendation.Api;

public record CreateRecommendationRequest(
    [Range(0, 1000000)] decimal ConsumptionKwh,
    [Required, StringLength(50)] string TaxGroup);

public record RecommendationResponse(
    Guid Id,
    decimal ConsumptionKwh,
    TaxGroupUsed TaxGroup,
    DateTimeOffset CalculatedAt,
    PlanQuoteResponse? Recommended,
    List<PlanQuoteResponse> Plans);

public record TaxGroupUsed(string Code, int VersionNo, decimal Vat, decimal EcoTax);

public record PlanQuoteResponse(Guid PlanId, int PlanVersionNo, string Name, decimal Total, BreakdownResponse Breakdown);

public record BreakdownResponse(
    List<TierChargeResponse> Tiers,
    decimal EnergySubtotal,
    decimal DiscountAmount,
    decimal NetEnergy,
    decimal EcoTax,
    decimal VatBase,
    decimal VatAmount,
    decimal Total);

public record TierChargeResponse(decimal Kwh, decimal PricePerKwh, decimal Amount);
