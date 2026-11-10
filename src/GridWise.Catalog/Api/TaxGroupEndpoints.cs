using GridWise.Catalog.Security;
using GridWise.Catalog.Services;
using Microsoft.AspNetCore.Mvc;
using static GridWise.Catalog.Api.EndpointHelpers;

namespace GridWise.Catalog.Api;

public static class TaxGroupEndpoints
{
    public static IEndpointRouteBuilder MapTaxGroupEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/tax-groups", CreateTaxGroup)
            .RequireAuthorization(ServiceAuthentication.AdminPolicy)
            .WithName("CreateTaxGroup")
            .WithTags("Tax groups")
            .Produces<TaxGroupResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        app.MapPost("/tax-groups/{code}/versions", AddVersion)
            .RequireAuthorization(ServiceAuthentication.AdminPolicy)
            .WithName("AddTaxGroupVersion")
            .WithTags("Tax groups")
            .Produces<TaxGroupResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        app.MapDelete("/tax-groups/{code}", RetireTaxGroup)
            .RequireAuthorization(ServiceAuthentication.AdminPolicy)
            .WithName("RetireTaxGroup")
            .WithTags("Tax groups")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        app.MapGet("/tax-groups", ListTaxGroups)
            .WithName("ListTaxGroups")
            .WithTags("Tax groups")
            .Produces<List<TaxGroupResponse>>();

        app.MapGet("/tax-groups/{code}", GetTaxGroup)
            .WithName("GetTaxGroup")
            .WithTags("Tax groups")
            .Produces<TaxGroupResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        app.MapGet("/tax-groups/{code}/versions", ListTaxGroupVersions)
            .WithName("ListTaxGroupVersions")
            .WithTags("Tax groups")
            .Produces<List<TaxGroupResponse>>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        app.MapGet("/tax-groups/{code}/versions/{versionNo:int}", GetTaxGroupVersion)
            .WithName("GetTaxGroupVersion")
            .WithTags("Tax groups")
            .Produces<TaxGroupResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> CreateTaxGroup(
        CreateTaxGroupRequest request,
        [FromHeader(Name = "X-Correlation-Id")] string? correlationId,
        TaxGroupService service,
        HttpContext http,
        CancellationToken ct)
    {
        correlationId = ResolveCorrelationId(correlationId);

        var result = await service.CreateAsync(request, correlationId, http.User.UserId(), ct);

        return ToResult(result, taxGroup =>
        {
            SetResponseHeaders(http, taxGroup.VersionNo, correlationId);
            return Results.Created($"/tax-groups/{taxGroup.Code}", taxGroup);
        });
    }

    private static async Task<IResult> AddVersion(
        string code,
        TaxGroupVersionRequest request,
        [FromHeader(Name = "If-Match")] string? ifMatch,
        [FromHeader(Name = "X-Correlation-Id")] string? correlationId,
        TaxGroupService service,
        HttpContext http,
        CancellationToken ct)
    {
        if (!TryParseIfMatch(ifMatch, out var expectedVersion))
            return InvalidIfMatch();

        correlationId = ResolveCorrelationId(correlationId);

        var result = await service.AddVersionAsync(code, expectedVersion, request, correlationId, http.User.UserId(), ct);

        return ToResult(result, taxGroup =>
        {
            SetResponseHeaders(http, taxGroup.VersionNo, correlationId);
            return Results.Created($"/tax-groups/{taxGroup.Code}/versions/{taxGroup.VersionNo}", taxGroup);
        });
    }

    private static async Task<IResult> RetireTaxGroup(
        string code,
        DateOnly? effectiveFrom,
        [FromHeader(Name = "If-Match")] string? ifMatch,
        [FromHeader(Name = "X-Correlation-Id")] string? correlationId,
        TaxGroupService service,
        HttpContext http,
        CancellationToken ct)
    {
        if (!TryParseIfMatch(ifMatch, out var expectedVersion))
            return InvalidIfMatch();

        correlationId = ResolveCorrelationId(correlationId);

        var result = await service.RetireAsync(code, expectedVersion, effectiveFrom, correlationId, http.User.UserId(), ct);

        return ToResult(result, taxGroup =>
        {
            SetResponseHeaders(http, taxGroup.VersionNo, correlationId);
            return Results.NoContent();
        });
    }

    private static async Task<IResult> ListTaxGroups(bool? includeRetired, TaxGroupService service, CancellationToken ct)
        => Results.Ok(await service.ListAsync(includeRetired ?? false, ct));

    private static async Task<IResult> GetTaxGroup(string code, TaxGroupService service, HttpContext http, CancellationToken ct)
    {
        var taxGroup = await service.GetLatestAsync(code, ct);
        if (taxGroup is null)
            return TaxGroupNotFound();

        http.Response.Headers.ETag = $"\"{taxGroup.VersionNo}\"";
        return Results.Ok(taxGroup);
    }

    private static async Task<IResult> ListTaxGroupVersions(string code, TaxGroupService service, CancellationToken ct)
    {
        var versions = await service.ListVersionsAsync(code, ct);
        return versions is null ? TaxGroupNotFound() : Results.Ok(versions);
    }

    private static async Task<IResult> GetTaxGroupVersion(string code, int versionNo, TaxGroupService service, CancellationToken ct)
    {
        var version = await service.GetVersionAsync(code, versionNo, ct);
        return version is null ? TaxGroupNotFound() : Results.Ok(version);
    }

    private static IResult ToResult(TaxGroupResult result, Func<TaxGroupResponse, IResult> onSuccess)
        => result switch
        {
            TaxGroupResult.Created created => onSuccess(created.TaxGroup),
            TaxGroupResult.ValidationFailed failed => Results.ValidationProblem(failed.Errors),
            TaxGroupResult.Conflict conflict => Results.Problem(
                statusCode: StatusCodes.Status409Conflict, title: "Conflict", detail: conflict.Reason),
            _ => TaxGroupNotFound()
        };

    private static IResult TaxGroupNotFound()
        => Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "The tax group was not found.");
}
