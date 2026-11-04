using GridWise.Catalog.Services;
using Microsoft.AspNetCore.Mvc;
using static GridWise.Catalog.Api.EndpointHelpers;

namespace GridWise.Catalog.Api;

public static class PlanEndpoints
{
    public static IEndpointRouteBuilder MapPlanEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/plans", CreatePlan)
            .WithName("CreatePlan")
            .WithTags("Plans")
            .Produces<PlanResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        app.MapPost("/plans/{id:guid}/versions", AddVersion)
            .WithName("AddPlanVersion")
            .WithTags("Plans")
            .Produces<PlanResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        app.MapDelete("/plans/{id:guid}", RetirePlan)
            .WithName("RetirePlan")
            .WithTags("Plans")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        app.MapGet("/plans", ListPlans)
            .WithName("ListPlans")
            .WithTags("Plans")
            .Produces<List<PlanResponse>>();

        app.MapGet("/plans/{id:guid}", GetPlan)
            .WithName("GetPlan")
            .WithTags("Plans")
            .Produces<PlanResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        app.MapGet("/plans/{id:guid}/versions", ListPlanVersions)
            .WithName("ListPlanVersions")
            .WithTags("Plans")
            .Produces<List<PlanResponse>>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        app.MapGet("/plans/{id:guid}/versions/{versionNo:int}", GetPlanVersion)
            .WithName("GetPlanVersion")
            .WithTags("Plans")
            .Produces<PlanResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

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
                SetResponseHeaders(http, created.Plan.VersionNo, correlationId);
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

    private static async Task<IResult> ListPlans(bool? includeRetired, PlanService service, CancellationToken ct)
        => Results.Ok(await service.ListAsync(includeRetired ?? false, ct));

    private static async Task<IResult> GetPlan(Guid id, PlanService service, HttpContext http, CancellationToken ct)
    {
        var plan = await service.GetLatestAsync(id, ct);
        if (plan is null)
            return PlanNotFound();

        http.Response.Headers.ETag = $"\"{plan.VersionNo}\"";
        return Results.Ok(plan);
    }

    private static async Task<IResult> ListPlanVersions(Guid id, PlanService service, CancellationToken ct)
    {
        var versions = await service.ListVersionsAsync(id, ct);
        return versions is null ? PlanNotFound() : Results.Ok(versions);
    }

    private static async Task<IResult> GetPlanVersion(Guid id, int versionNo, PlanService service, CancellationToken ct)
    {
        var version = await service.GetVersionAsync(id, versionNo, ct);
        return version is null ? PlanNotFound() : Results.Ok(version);
    }

    private static IResult PlanNotFound()
        => Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "The plan was not found.");

    private static IResult ToResult(ChangePlanResult result, Func<PlanResponse, IResult> onSuccess)
        => result switch
        {
            ChangePlanResult.Created created => onSuccess(created.Plan),
            ChangePlanResult.ValidationFailed failed => Results.ValidationProblem(failed.Errors),
            ChangePlanResult.Conflict conflict => Results.Problem(
                statusCode: StatusCodes.Status409Conflict, title: "Conflict", detail: conflict.Reason),
            _ => PlanNotFound()
        };
}
