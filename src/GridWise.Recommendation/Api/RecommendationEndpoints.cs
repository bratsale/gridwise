using System.Globalization;
using GridWise.Recommendation.Security;
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

        app.MapPost("/recommendations/{id:guid}/accept", AcceptPlan)
            .WithName("AcceptPlan")
            .WithTags("Recommendations")
            .Produces<AcceptanceResponse>(StatusCodes.Status201Created)
            .Produces<AcceptanceResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

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

        var result = await service.CalculateAsync(request, correlationId, http.User.UserId(), ct);

        switch (result)
        {
            case CalculateResult.Created created:
                http.Response.Headers["X-Correlation-Id"] = correlationId;
                http.Response.Headers["Server-Timing"] = ServerTiming(created.Timings);
                return Results.Created($"/recommendations/{created.Recommendation.Id}", created.Recommendation);

            case CalculateResult.ValidationFailed failed:
                return Results.ValidationProblem(failed.Errors);

            default:
                return Results.Problem(
                    statusCode: StatusCodes.Status503ServiceUnavailable,
                    title: "no plans available yet");
        }
    }

    private static string ServerTiming(CalculationTimings t) => string.Create(CultureInfo.InvariantCulture,
        $"db-read;dur={t.DbRead:F1}, calc;dur={t.Calc:F1}, json;dur={t.Json:F1}, save;dur={t.Save:F1}");

    private static async Task<IResult> AcceptPlan(
        Guid id,
        AcceptPlanRequest request,
        [FromHeader(Name = "X-Correlation-Id")] string? correlationId,
        RecommendationService service,
        HttpContext http,
        CancellationToken ct)
    {
        correlationId = ResolveCorrelationId(correlationId);

        var result = await service.AcceptAsync(id, request, correlationId, http.User.UserId(), ct);
        http.Response.Headers["X-Correlation-Id"] = correlationId;

        return result switch
        {
            AcceptResult.Created created => Results.Created($"/recommendations/{id}", created.Acceptance),
            AcceptResult.AlreadyAccepted existing => Results.Ok(existing.Acceptance),
            AcceptResult.ValidationFailed failed => Results.ValidationProblem(failed.Errors),
            AcceptResult.Conflict => Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "A different plan was already accepted for this recommendation."),
            _ => Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Recommendation not found.")
        };
    }

    private static async Task<IResult> GetRecommendation(Guid id, RecommendationService service, HttpContext http, CancellationToken ct)
    {
        var recommendation = await service.GetAsync(id, http.User.UserId(), http.User.IsInRole(ServiceAuthentication.AdminRole), ct);

        return recommendation is null
            ? Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Recommendation not found.")
            : Results.Ok(recommendation);
    }
}
