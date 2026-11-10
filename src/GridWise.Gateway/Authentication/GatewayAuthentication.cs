using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;
using Yarp.ReverseProxy.Transforms;
using Yarp.ReverseProxy.Transforms.Builder;

namespace GridWise.Gateway.Authentication;

/// <summary>
/// Bearer tokens from Keycloak. The gateway checks that a request comes with a valid token (signature, issuer, audience gridwise-api,
/// lifetime) and forwards the Authorization header unchanged; what a user may do (roles) is decided by the services.
/// </summary>
public static partial class GatewayAuthentication
{
    public const string QueryTokenParameter = "access_token";
    private const string QueryTokenItem = "GridWise.QueryToken";

    // The progress stream: EventSource in the browser cannot send headers, so only here the token may come in the query string.
    [GeneratedRegex("^/api/batch/jobs/[^/]+/events$")]
    private static partial Regex EventsPath();

    public static bool IsEventsRequest(HttpRequest request)
        => HttpMethods.IsGet(request.Method) && EventsPath().IsMatch(request.Path.Value ?? "");

    public static IHostApplicationBuilder AddGatewayAuthentication(this IHostApplicationBuilder builder)
    {
        var jwt = builder.Configuration.GetSection("Jwt");
        var issuer = jwt["Issuer"] ?? throw new InvalidOperationException("Jwt:Issuer is not configured.");
        var audience = jwt["Audience"] ?? throw new InvalidOperationException("Jwt:Audience is not configured.");

        builder.Services.AddProblemDetails();
        builder.Services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                // The keys come from Keycloak's internal address; the issuer in the tokens is the public one (what the browser uses).
                options.MetadataAddress = jwt["MetadataAddress"] ?? throw new InvalidOperationException("Jwt:MetadataAddress is not configured.");
                options.RequireHttpsMetadata = jwt.GetValue("RequireHttpsMetadata", true);
                // After Keycloak is recreated (dev mode starts with a new embedded database, so it has new signing keys), tokens
                // carry a key id the cached keys do not know. With RefreshOnIssuerKeyNotFound the handler then fetches the keys
                // again, but at most once per RefreshInterval; the default of 5 minutes would reject every token for up to
                // 5 minutes. 30 s keeps that window short, and still limits how often a token with an unknown key id (a forged
                // one, for example) can make the service call Keycloak.
                options.RefreshOnIssuerKeyNotFound = true;
                options.RefreshInterval = TimeSpan.FromSeconds(jwt.GetValue("KeyRefreshIntervalSeconds", 30));
                options.MapInboundClaims = false;
                options.TokenValidationParameters.ValidIssuer = issuer;
                options.TokenValidationParameters.ValidAudience = audience; // one of the token's audiences must match
                options.TokenValidationParameters.ValidateLifetime = true;
                options.TokenValidationParameters.ClockSkew = TimeSpan.FromSeconds(jwt.GetValue("ClockSkewSeconds", 30));

                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        var request = context.Request;
                        if (StringValues.IsNullOrEmpty(request.Headers.Authorization)
                            && IsEventsRequest(request)
                            && request.Query.TryGetValue(QueryTokenParameter, out var token)
                            && !StringValues.IsNullOrEmpty(token))
                        {
                            context.Token = token.ToString();
                            context.HttpContext.Items[QueryTokenItem] = context.Token; // forwarded as a header, see AddQueryTokenForwarding
                        }
                        return Task.CompletedTask;
                    },
                    OnChallenge = WriteUnauthorizedAsync
                };
            });

        // Every endpoint needs an authenticated user unless it says otherwise (the health checks do): a route added to the
        // configuration later is protected without anyone having to remember it.
        builder.Services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

        return builder;
    }

    /// <summary>
    /// On the events route, a token that came in the query string goes to batch as an Authorization header, and access_token
    /// is removed from the forwarded URL. A token in the header is forwarded unchanged (YARP copies the header).
    /// </summary>
    public static void AddQueryTokenForwarding(TransformBuilderContext context)
        => context.AddRequestTransform(transform =>
        {
            transform.Query.Collection.Remove(QueryTokenParameter);
            if (transform.HttpContext.Items[QueryTokenItem] is string token)
                transform.ProxyRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return ValueTask.CompletedTask;
        });

    // 401 as ProblemDetails, with the WWW-Authenticate header a client needs (RFC 6750).
    private static async Task WriteUnauthorizedAsync(JwtBearerChallengeContext context)
    {
        context.HandleResponse();

        var challenge = "Bearer";
        if (context.AuthenticateFailure is not null)
            challenge += $" error=\"invalid_token\", error_description=\"{Quote(context.ErrorDescription ?? "The token is not valid.")}\"";

        var http = context.HttpContext;
        http.Response.StatusCode = StatusCodes.Status401Unauthorized;
        http.Response.Headers.WWWAuthenticate = challenge;

        var problems = http.RequestServices.GetRequiredService<IProblemDetailsService>();
        await problems.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = http,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "Unauthorized",
                Detail = context.AuthenticateFailure is null
                    ? "A bearer token for gridwise-api is required."
                    : "The bearer token is not valid (signature, issuer, audience or lifetime)."
            }
        });
    }

    private static string Quote(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
