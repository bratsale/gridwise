namespace GridWise.Pricing;

// UpTo = upper limit, '?' is for the last tier (no upper limit)
public sealed record PricingTier(decimal? UpTo, decimal PricePerKwh);

