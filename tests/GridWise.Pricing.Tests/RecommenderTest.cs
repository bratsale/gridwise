namespace GridWise.Pricing.Tests;

public class RecommenderTests
{
    [Fact]
    public void Plans_are_sorted_by_total_cheapest_first()
    {
        var ranked = Recommender.Rank(
            [PriceCalculatorTests.Standard, PriceCalculatorTests.Premium],
            PriceCalculatorTests.Household, 250m);

        Assert.Equal("Premium,Standard", string.Join(",", ranked.Select(r => r.Plan.Name)));
        Assert.Equal(24.10m, ranked[0].Breakdown.Total);
        Assert.Equal(25.92m, ranked[1].Breakdown.Total);
    }

    [Fact]
    public void Equal_totals_are_ordered_by_plan_name()
    {
        var b = PriceCalculatorTests.Standard with { Name = "B" };
        var a = PriceCalculatorTests.Standard with { Name = "A" };

        var ranked = Recommender.Rank([b, a], PriceCalculatorTests.Household, 250m);

        Assert.Equal("A,B", string.Join(",", ranked.Select(r => r.Plan.Name)));
    }

    [Fact]
    public void No_plans_gives_empty_result()
        => Assert.Empty(Recommender.Rank([], PriceCalculatorTests.Household, 250m));
}