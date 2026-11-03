using GridWise.Catalog.Services;
using Microsoft.AspNetCore.Mvc;

namespace GridWise.Catalog.Api;

public static class PlanEndpoints
{
    public static IEndpointRouteBuilder MapPlanEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/plans", CreatePlan);
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

        correlationId = string.IsNullOrWhiteSpace(correlationId) ? Guid.NewGuid().ToString() : correlationId;

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
}
