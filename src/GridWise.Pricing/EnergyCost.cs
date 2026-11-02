namespace GridWise.Pricing;

public static class EnergyCost
{
    public static decimal Calculate(IReadOnlyList<PricingTier> tiers, decimal kwh)
        => Charges(tiers, kwh).Sum(c => c.Amount);

    public static IReadOnlyList<TierCharge> Charges(IReadOnlyList<PricingTier> tiers, decimal kwh)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(kwh);

        decimal remaining = kwh;
        decimal previousLimit = 0m;
        var charges = new List<TierCharge>();

        foreach (var tier in tiers)
        {
            if (tier.UpTo is not decimal limit)
            {
                // The last tier (no upper limit) takes everything left
                charges.Add(Charge(remaining, tier));
                return charges;
            }

            decimal taken = Math.Min(remaining, limit - previousLimit);

            charges.Add(Charge(taken, tier));
            remaining -= taken;

            if (remaining == 0m)
                return charges;

            previousLimit = limit;
        }

        throw new InvalidOperationException("The last tier must have no upper limit.");
    }

    private static TierCharge Charge(decimal kwh, PricingTier tier)
        => new(kwh, tier.PricePerKwh, Money.Round(kwh * tier.PricePerKwh));
}
