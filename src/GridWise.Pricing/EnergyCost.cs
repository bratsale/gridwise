namespace GridWise.Pricing;

public static class EnergyCost
{
    public static decimal Calculate(IReadOnlyList<PricingTier> tiers, decimal kwh)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(kwh);

        decimal remaining = kwh;
        decimal previousLimit = 0m;
        decimal total = 0m;

        foreach (var tier in tiers)
        {
            // Width of this tier; the last tier (no upper limit) takes everything left
            decimal width = tier.UpTo is null ? remaining : tier.UpTo.Value - previousLimit;
            decimal taken = Math.Min(remaining, width);

            total += Money.Round(taken * tier.PricePerKwh);
            remaining -= taken;

            if (remaining == 0m)
                return total;

            previousLimit = tier.UpTo.Value;
        }

        throw new InvalidOperationException("The last tier must have no upper limit.");
    }
}