using GridWise.Catalog.Domain;

namespace GridWise.Catalog.Tests;

public class CatalogRulesTests
{
    private static readonly DateOnly Today = new(2026, 11, 3);

    private static readonly TierDraft[] StandardTiers =
    [
        new(100m, 0.10m), new(300m, 0.08m), new(500m, 0.07m), new(null, 0.06m)
    ];

    private static PlanDraft Standard => new("Standard", 0.05m, Today, StandardTiers);

    private static void AssertSingleError(Dictionary<string, string[]> errors, string key)
    {
        var error = Assert.Single(errors);
        Assert.Equal(key, error.Key);
        Assert.Single(error.Value);
    }

    [Fact]
    public void Valid_plan_has_no_errors()
        => Assert.Empty(CatalogRules.ValidatePlan(Standard, Today, null));

    [Fact]
    public void Empty_tiers()
        => AssertSingleError(CatalogRules.ValidateTiers([]), "tiers");

    [Fact]
    public void Last_tier_with_threshold()
        => AssertSingleError(
            CatalogRules.ValidateTiers([new(100m, 0.10m), new(300m, 0.08m)]),
            "tiers[1].threshold");

    [Fact]
    public void Null_threshold_not_last()
        => AssertSingleError(
            CatalogRules.ValidateTiers([new(100m, 0.10m), new(null, 0.08m), new(null, 0.06m)]),
            "tiers[1].threshold");

    [Fact]
    public void Equal_thresholds()
        => AssertSingleError(
            CatalogRules.ValidateTiers([new(100m, 0.10m), new(100m, 0.08m), new(null, 0.06m)]),
            "tiers[1].threshold");

    [Fact]
    public void Descending_thresholds()
        => AssertSingleError(
            CatalogRules.ValidateTiers([new(300m, 0.10m), new(100m, 0.08m), new(null, 0.06m)]),
            "tiers[1].threshold");

    [Fact]
    public void Threshold_zero()
        => AssertSingleError(
            CatalogRules.ValidateTiers([new(0m, 0.10m), new(300m, 0.08m), new(null, 0.06m)]),
            "tiers[0].threshold");

    [Fact]
    public void Negative_price()
        => AssertSingleError(
            CatalogRules.ValidateTiers([new(100m, -0.10m), new(null, 0.06m)]),
            "tiers[0].pricePerKwh");

    [Fact]
    public void Effective_from_yesterday()
        => AssertSingleError(
            CatalogRules.ValidateEffectiveFrom(Today.AddDays(-1), Today, null),
            "effectiveFrom");

    [Fact]
    public void Effective_from_today_is_valid()
        => Assert.Empty(CatalogRules.ValidateEffectiveFrom(Today, Today, null));

    [Fact]
    public void Effective_from_before_previous()
        => AssertSingleError(
            CatalogRules.ValidateEffectiveFrom(Today.AddDays(1), Today, Today.AddDays(2)),
            "effectiveFrom");

    [Fact]
    public void Effective_from_equal_to_previous_is_valid()
        => Assert.Empty(CatalogRules.ValidateEffectiveFrom(Today.AddDays(2), Today, Today.AddDays(2)));

    [Fact]
    public void Plan_with_two_problems_returns_both_errors()
    {
        var draft = Standard with
        {
            EffectiveFrom = Today.AddDays(-1),
            Tiers = [new(100m, -0.10m), new(null, 0.06m)]
        };

        var errors = CatalogRules.ValidatePlan(draft, Today, null);

        Assert.Equal(2, errors.Count);
        Assert.Contains("effectiveFrom", errors.Keys);
        Assert.Contains("tiers[0].pricePerKwh", errors.Keys);
    }
}
