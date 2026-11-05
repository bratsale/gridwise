using GridWise.Recommendation.Services;
using Microsoft.AspNetCore.Mvc;
using static GridWise.Recommendation.Api.EndpointHelpers;

namespace GridWise.Recommendation.Api;

public static class RecommendationEndpoints
{
    public static IEndpointRouteBuilder MapRecommendationEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/recommendations", CreateRecommendation)
            .WithName("CreateRecommendation")
            .WithTags("Recommendations")
            .Produces<RecommendationResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        app.MapGet("/recommendations/{id:guid}", GetRecommendation)
            .WithName("GetRecommendation")
            .WithTags("Recommendations")
            .Produces<RecommendationResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> CreateRecommendation(
        CreateRecommendationRequest request,
        [FromHeader(Name = "X-Correlation-Id")] string? correlationId,
        RecommendationService service,
        HttpContext http,
        CancellationToken ct)
    {
        correlationId = ResolveCorrelationId(correlationId);

        var result = await service.CalculateAsync(request, correlationId, ct);

        switch (result)
        {
            case CalculateResult.Created created:
                http.Response.Headers["X-Correlation-Id"] = correlationId;
                return Results.Created($"/recommendations/{created.Recommendation.Id}", created.Recommendation);

            case CalculateResult.ValidationFailed failed:
                return Results.ValidationProblem(failed.Errors);

            default:
                return Results.Problem(
                    statusCode: StatusCodes.Status503ServiceUnavailable,
                    title: "no plans available yet");
        }
    }

    private static async Task<IResult> GetRecommendation(Guid id, RecommendationService service, CancellationToken ct)
    {
        var recommendation = await service.GetAsync(id, ct);

        return recommendation is null
            ? Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Recommendation not found.")
            : Results.Ok(recommendation);
    }
}
