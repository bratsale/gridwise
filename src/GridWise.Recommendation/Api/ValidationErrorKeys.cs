using System.Text.Json;

namespace GridWise.Recommendation.Api;

// Validation errors are keyed by the JSON (camelCase) names of the fields, like the hand-written checks and the request bodies.
// The built-in validation (AddValidation) reports the C# member names ("ConsumptionKwh", "Tiers[0].Threshold") and has no
// naming option in .NET 10, so every validation problem is renamed on its way out: "consumptionKwh", "tiers[0].threshold".
public static class ValidationErrorKeys
{
    public static void UseCamelCase(ProblemDetailsContext context)
    {
        if (context.ProblemDetails is not HttpValidationProblemDetails validation) return;

        var errors = validation.Errors.ToList();
        validation.Errors.Clear();
        foreach (var (key, messages) in errors)
        {
            var name = string.Join('.', key.Split('.').Select(JsonNamingPolicy.CamelCase.ConvertName));
            validation.Errors[name] = validation.Errors.TryGetValue(name, out var earlier) ? [.. earlier, .. messages] : messages;
        }
    }
}
