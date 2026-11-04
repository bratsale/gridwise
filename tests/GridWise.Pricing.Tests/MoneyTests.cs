namespace GridWise.Pricing.Tests;

public class MoneyTests
{
    public static TheoryData<decimal, decimal> Cases => new()
    {
        { 0.125m, 0.13m }, { 2.485m, 2.49m }, { 0.005m, 0.01m },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Midpoints_round_away_from_zero(decimal amount, decimal expected)
        => Assert.Equal(expected, Money.Round(amount));
}
