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

    // Each broken tier list gives exactly one error, on the field the form shows it next to.
    private static readonly Dictionary<string, TierDraft[]> InvalidTiers = new()
    {
        ["no tiers"] = [],
        ["the last tier has a limit"] = [new(100m, 0.10m), new(300m, 0.08m)],
        ["a tier without a limit that is not the last"] = [new(100m, 0.10m), new(null, 0.08m), new(null, 0.06m)],
        ["two tiers with the same limit"] = [new(100m, 0.10m), new(100m, 0.08m), new(null, 0.06m)],
        ["limits going down"] = [new(300m, 0.10m), new(100m, 0.08m), new(null, 0.06m)],
        ["a limit of 0"] = [new(0m, 0.10m), new(300m, 0.08m), new(null, 0.06m)],
        ["a negative price"] = [new(100m, -0.10m), new(null, 0.06m)],
    };

    [Theory]
    [InlineData("no tiers", "tiers")]
    [InlineData("the last tier has a limit", "tiers[1].threshold")]
    [InlineData("a tier without a limit that is not the last", "tiers[1].threshold")]
    [InlineData("two tiers with the same limit", "tiers[1].threshold")]
    [InlineData("limits going down", "tiers[1].threshold")]
    [InlineData("a limit of 0", "tiers[0].threshold")]
    [InlineData("a negative price", "tiers[0].pricePerKwh")]
    public void Invalid_tiers(string tiers, string errorOn)
        => AssertSingleError(CatalogRules.ValidateTiers(InvalidTiers[tiers]), errorOn);

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
