namespace GridWise.Pricing.Tests;

public class PriceCalculatorTests
{
    private static readonly PricingPlan Standard = new("Standard",
        [new(100m, 0.10m), new(300m, 0.08m), new(500m, 0.07m), new(null, 0.06m)], 0.05m);

    private static readonly PricingPlan Premium = new("Premium",
        [new(200m, 0.09m), new(400m, 0.07m), new(600m, 0.05m), new(null, 0.04m)], 0.10m);

    private static readonly TaxGroup Household = new(0.17m, 0.005m);
    private static readonly TaxGroup Business = new(0.20m, 0.010m);

    [Fact]
    public void Premium_250_household_full_breakdown()
    {
        var b = PriceCalculator.Calculate(Premium, Household, 250m);
        Assert.Equal(21.50m, b.EnergySubtotal);
        Assert.Equal(2.15m, b.DiscountAmount);
        Assert.Equal(19.35m, b.NetEnergy);
        Assert.Equal(1.25m, b.EcoTax);
        Assert.Equal(20.60m, b.VatBase);
        Assert.Equal(3.50m, b.VatAmount);
        Assert.Equal(24.10m, b.Total);
    }

    [Fact]
    public void Standard_250_household_full_breakdown()
    {
        var b = PriceCalculator.Calculate(Standard, Household, 250m);
        Assert.Equal(22.00m, b.EnergySubtotal);
        Assert.Equal(1.10m, b.DiscountAmount);
        Assert.Equal(20.90m, b.NetEnergy);
        Assert.Equal(1.25m, b.EcoTax);
        Assert.Equal(22.15m, b.VatBase);
        Assert.Equal(3.77m, b.VatAmount);
        Assert.Equal(25.92m, b.Total);
    }

    [Fact] public void Premium_250_business_total() => AssertTotal(Premium, Business, 250m, 26.22m);
    [Fact] public void Standard_250_business_total() => AssertTotal(Standard, Business, 250m, 28.08m);
    [Fact] public void Standard_600_household_total() => AssertTotal(Standard, Household, 600m, 54.64m);
    [Fact] public void Premium_600_household_total() => AssertTotal(Premium, Household, 600m, 47.74m);

    private static void AssertTotal(PricingPlan plan, TaxGroup group, decimal kwh, decimal expected)
        => Assert.Equal(expected, PriceCalculator.Calculate(plan, group, kwh).Total);
}