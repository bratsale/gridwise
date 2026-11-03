using System.ComponentModel.DataAnnotations;

namespace GridWise.Catalog.Api;

public record CreatePlanRequest(
    [Required, StringLength(100, MinimumLength = 1)] string Name,
    [Range(0, 1)] decimal Discount,
    DateOnly? EffectiveFrom,
    List<TierRequest> Tiers);

public record TierRequest(decimal? Threshold, decimal PricePerKwh);

public record PlanResponse(
    Guid Id,
    int VersionNo,
    string Name,
    decimal Discount,
    string Status,
    DateOnly EffectiveFrom,
    List<TierRequest> Tiers);
