namespace GridWise.Pricing;

public sealed record PricingPlan(string Name, IReadOnlyList<PricingTier> Tiers, decimal DiscountRate);

public sealed record TaxGroup(decimal VatRate, decimal EcoTaxPerKwh);

public sealed record TierCharge(decimal Kwh, decimal PricePerKwh, decimal Amount);

public sealed record PriceBreakdown(
    IReadOnlyList<TierCharge> Tiers,
    decimal EnergySubtotal,
    decimal DiscountAmount,
    decimal NetEnergy,
    decimal EcoTax,
    decimal VatBase,
    decimal VatAmount,
    decimal Total);

public static class PriceCalculator
{
    public static PriceBreakdown Calculate(PricingPlan plan, TaxGroup taxGroup, decimal kwh)
    {
        var tiers = EnergyCost.Charges(plan.Tiers, kwh);
        var subtotal = tiers.Sum(t => t.Amount);
        var discount = Money.Round(subtotal * plan.DiscountRate);
        var net = subtotal - discount;
        var eco = Money.Round(kwh * taxGroup.EcoTaxPerKwh);
        var vatBase = net + eco;
        var vat = Money.Round(vatBase * taxGroup.VatRate);

        return new PriceBreakdown(tiers, subtotal, discount, net, eco, vatBase, vat, vatBase + vat);
    }
}