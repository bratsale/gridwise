using System.Globalization;
using GridWise.Catalog.Services;
using Microsoft.AspNetCore.Mvc;

namespace GridWise.Catalog.Api;

public static class PlanEndpoints
{
    public static IEndpointRouteBuilder MapPlanEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/plans", CreatePlan);
        app.MapPost("/plans/{id:guid}/versions", AddVersion);
        app.MapDelete("/plans/{id:guid}", RetirePlan);
        return app;
    }

    private static async Task<IResult> CreatePlan(
        CreatePlanRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        [FromHeader(Name = "X-Correlation-Id")] string? correlationId,
        PlanService service,
        HttpContext http,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "The Idempotency-Key header is required.");

        if (idempotencyKey.Length > 100)
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "The Idempotency-Key header must be at most 100 characters.");

        correlationId = ResolveCorrelationId(correlationId);

        var result = await service.CreateAsync(request, idempotencyKey, correlationId, ct);

        switch (result)
        {
            case CreatePlanResult.Created created:
                http.Response.Headers.ETag = "\"1\"";
                http.Response.Headers["X-Correlation-Id"] = correlationId;
                return Results.Created($"/plans/{created.Plan.Id}", created.Plan);

            case CreatePlanResult.ValidationFailed failed:
                return Results.ValidationProblem(failed.Errors);

            default:
                return Results.Problem(
                    statusCode: StatusCodes.Status422UnprocessableEntity,
                    title: "The Idempotency-Key was already used with a different request body.");
        }
    }

    private static async Task<IResult> AddVersion(
        Guid id,
        CreatePlanRequest request,
        [FromHeader(Name = "If-Match")] string? ifMatch,
        [FromHeader(Name = "X-Correlation-Id")] string? correlationId,
        PlanService service,
        HttpContext http,
        CancellationToken ct)
    {
        if (!TryParseIfMatch(ifMatch, out var expectedVersion))
            return InvalidIfMatch();

        correlationId = ResolveCorrelationId(correlationId);

        var result = await service.AddVersionAsync(id, expectedVersion, request, correlationId, ct);

        return ToResult(result, plan =>
        {
            SetResponseHeaders(http, plan.VersionNo, correlationId);
            return Results.Created($"/plans/{plan.Id}/versions/{plan.VersionNo}", plan);
        });
    }

    private static async Task<IResult> RetirePlan(
        Guid id,
        DateOnly? effectiveFrom,
        [FromHeader(Name = "If-Match")] string? ifMatch,
        [FromHeader(Name = "X-Correlation-Id")] string? correlationId,
        PlanService service,
        HttpContext http,
        CancellationToken ct)
    {
        if (!TryParseIfMatch(ifMatch, out var expectedVersion))
            return InvalidIfMatch();

        correlationId = ResolveCorrelationId(correlationId);

        var result = await service.RetireAsync(id, expectedVersion, effectiveFrom, correlationId, ct);

        return ToResult(result, plan =>
        {
            SetResponseHeaders(http, plan.VersionNo, correlationId);
            return Results.NoContent();
        });
    }

    private static IResult ToResult(ChangePlanResult result, Func<PlanResponse, IResult> onSuccess)
        => result switch
        {
            ChangePlanResult.Created created => onSuccess(created.Plan),
            ChangePlanResult.ValidationFailed failed => Results.ValidationProblem(failed.Errors),
            ChangePlanResult.Conflict conflict => Results.Problem(
                statusCode: StatusCodes.Status409Conflict, title: "Conflict", detail: conflict.Reason),
            _ => Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "The plan was not found.")
        };

    private static string ResolveCorrelationId(string? correlationId)
        => string.IsNullOrWhiteSpace(correlationId) ? Guid.NewGuid().ToString() : correlationId;

    private static void SetResponseHeaders(HttpContext http, int versionNo, string correlationId)
    {
        http.Response.Headers.ETag = $"\"{versionNo}\"";
        http.Response.Headers["X-Correlation-Id"] = correlationId;
    }

    private static bool TryParseIfMatch(string? ifMatch, out int version)
    {
        version = 0;
        return ifMatch is not null
            && int.TryParse(ifMatch.Trim().Trim('"'), NumberStyles.None, CultureInfo.InvariantCulture, out version);
    }

    private static IResult InvalidIfMatch()
        => Results.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "The If-Match header is required and must contain the current version number.");
}
