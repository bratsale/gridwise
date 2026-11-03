using System.Globalization;

namespace GridWise.Catalog.Api;

public static class EndpointHelpers
{
    public static string ResolveCorrelationId(string? correlationId)
        => string.IsNullOrWhiteSpace(correlationId) ? Guid.NewGuid().ToString() : correlationId;

    public static void SetResponseHeaders(HttpContext http, int versionNo, string correlationId)
    {
        http.Response.Headers.ETag = $"\"{versionNo}\"";
        http.Response.Headers["X-Correlation-Id"] = correlationId;
    }

    public static bool TryParseIfMatch(string? ifMatch, out int version)
    {
        version = 0;
        return ifMatch is not null
            && int.TryParse(ifMatch.Trim().Trim('"'), NumberStyles.None, CultureInfo.InvariantCulture, out version);
    }

    public static IResult InvalidIfMatch()
        => Results.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "The If-Match header is required and must contain the current version number.");
}
