namespace GridWise.Catalog.Domain;

public static class CatalogRules
{
    public static Dictionary<string, string[]> ValidateEffectiveFrom(
        DateOnly effectiveFrom, DateOnly today, DateOnly? previousEffectiveFrom)
    {
        var errors = new Dictionary<string, List<string>>();

        if (effectiveFrom < today)
            Add(errors, "effectiveFrom", "Effective date cannot be in the past.");

        if (previousEffectiveFrom is DateOnly previous && effectiveFrom < previous)
            Add(errors, "effectiveFrom", "Effective date cannot be before the previous version's effective date.");

        return ToResult(errors);
    }

    public static Dictionary<string, string[]> ValidateTiers(IReadOnlyList<TierDraft> tiers)
    {
        var errors = new Dictionary<string, List<string>>();

        if (tiers.Count == 0)
        {
            Add(errors, "tiers", "At least one tier is required.");
            return ToResult(errors);
        }

        decimal? previousThreshold = null;

        for (var i = 0; i < tiers.Count; i++)
        {
            var tier = tiers[i];
            var isLast = i == tiers.Count - 1;

            if (tier.PricePerKwh < 0)
                Add(errors, $"tiers[{i}].pricePerKwh", "Price per kWh cannot be negative.");

            if (tier.Threshold is not decimal threshold)
            {
                if (!isLast)
                    Add(errors, $"tiers[{i}].threshold", "Only the last tier can have no threshold.");
                continue;
            }

            if (isLast)
                Add(errors, $"tiers[{i}].threshold", "The last tier must have no threshold.");

            if (threshold <= 0)
                Add(errors, $"tiers[{i}].threshold", "Threshold must be greater than zero.");

            if (previousThreshold is decimal previous && threshold <= previous)
                Add(errors, $"tiers[{i}].threshold", "Thresholds must be strictly ascending.");

            previousThreshold = threshold;
        }

        return ToResult(errors);
    }

    public static Dictionary<string, string[]> ValidatePlan(
        PlanDraft draft, DateOnly today, DateOnly? previousEffectiveFrom)
    {
        var errors = ValidateEffectiveFrom(draft.EffectiveFrom, today, previousEffectiveFrom);

        foreach (var (key, messages) in ValidateTiers(draft.Tiers))
            errors[key] = messages;

        return errors;
    }

    private static void Add(Dictionary<string, List<string>> errors, string key, string message)
    {
        if (!errors.TryGetValue(key, out var messages))
            errors[key] = messages = [];

        messages.Add(message);
    }

    private static Dictionary<string, string[]> ToResult(Dictionary<string, List<string>> errors)
        => errors.ToDictionary(e => e.Key, e => e.Value.ToArray());
}
