using GridWise.Pricing;

namespace GridWise.Pricing.Tests;

public class EnergyCostTests
{
    private static readonly PricingTier[] Standard =
    [
        new(100m, 0.10m), new(300m, 0.08m), new(500m, 0.07m), new(null, 0.06m)
    ];

    public static TheoryData<decimal, decimal> Cases => new()
    {
        { 0m, 0.00m },   { 100m, 10.00m }, { 250m, 22.00m }, { 300m, 26.00m },
        { 301m, 26.07m }, { 500m, 40.00m }, { 600m, 46.00m },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Standard_plan_energy_cost(decimal kwh, decimal expected)
        => Assert.Equal(expected, EnergyCost.Calculate(Standard, kwh));
    
    [Fact]
    public void Negative_consumption_is_rejected()
        => Assert.Throws<ArgumentOutOfRangeException>(() => EnergyCost.Calculate(Standard, -1m));
}