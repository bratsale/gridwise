namespace GridWise.Pricing;

public static class Money
{
    // Two decimals, half rounds away from zero
    public static decimal Round(decimal amount)
        => Math.Round(amount, 2, MidpointRounding.AwayFromZero);
}