using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GridWise.Recommendation.Security;

/// <summary>
/// The service checks the bearer token itself, with the same settings as the gateway (defense in depth: a request that reaches the
/// service without passing the gateway is still checked), and decides what the user may do from the realm roles in the token.
/// </summary>
public static class ServiceAuthentication
{
    public const string AdminPolicy = "admin";
    public const string CustomerOrAdminPolicy = "customer-or-admin";
    public const string AdminRole = "admin";
    public const string CustomerRole = "customer";

    public static IHostApplicationBuilder AddServiceAuthentication(this IHostApplicationBuilder builder)
    {
        var jwt = builder.Configuration.GetSection("Jwt");

        builder.Services.AddProblemDetails();
        builder.Services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MetadataAddress = jwt["MetadataAddress"] ?? throw new InvalidOperationException("Jwt:MetadataAddress is not configured.");
                options.RequireHttpsMetadata = jwt.GetValue("RequireHttpsMetadata", true);
                options.MapInboundClaims = false;
                options.TokenValidationParameters.ValidIssuer = jwt["Issuer"] ?? throw new InvalidOperationException("Jwt:Issuer is not configured.");
                options.TokenValidationParameters.ValidAudience = jwt["Audience"] ?? throw new InvalidOperationException("Jwt:Audience is not configured.");
                options.TokenValidationParameters.ValidateLifetime = true;
                options.TokenValidationParameters.ClockSkew = TimeSpan.FromSeconds(jwt.GetValue("ClockSkewSeconds", 30));
                options.TokenValidationParameters.NameClaimType = "preferred_username";
                options.TokenValidationParameters.RoleClaimType = "role";

                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = context =>
                    {
                        AddRealmRoles(context.Principal);
                        return Task.CompletedTask;
                    },
                    OnChallenge = context =>
                    {
                        context.HandleResponse();
                        var challenge = context.AuthenticateFailure is null
                            ? "Bearer"
                            : "Bearer error=\"invalid_token\"";
                        context.Response.Headers.WWWAuthenticate = challenge;
                        return WriteProblemAsync(context.HttpContext, StatusCodes.Status401Unauthorized, "Unauthorized",
                            "A valid bearer token is required.");
                    },
                    OnForbidden = context => WriteProblemAsync(context.HttpContext, StatusCodes.Status403Forbidden, "Forbidden",
                        "Your role does not allow this.")
                };
            });

        // Everything needs an authenticated user unless it says otherwise (health, OpenAPI and the API reference do).
        builder.Services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
            .AddPolicy(AdminPolicy, policy => policy.RequireRole(AdminRole))
            .AddPolicy(CustomerOrAdminPolicy, policy => policy.RequireRole(CustomerRole, AdminRole));

        return builder;
    }

    /// <summary>The user's id: the token's sub claim.</summary>
    public static string? UserId(this ClaimsPrincipal user) => user.FindFirst("sub")?.Value;

    // Keycloak puts the realm roles in realm_access.roles (a JSON object claim); ASP.NET checks roles on "role" claims.
    private static void AddRealmRoles(ClaimsPrincipal? principal)
    {
        if (principal?.Identity is not ClaimsIdentity identity || identity.FindFirst("realm_access")?.Value is not { } realmAccess)
            return;

        using var json = JsonDocument.Parse(realmAccess);
        if (!json.RootElement.TryGetProperty("roles", out var roles) || roles.ValueKind != JsonValueKind.Array)
            return;

        foreach (var role in roles.EnumerateArray())
        {
            if (role.GetString() is { } name)
                identity.AddClaim(new Claim("role", name));
        }
    }

    private static Task WriteProblemAsync(HttpContext http, int status, string title, string detail)
    {
        http.Response.StatusCode = status;
        return http.RequestServices.GetRequiredService<IProblemDetailsService>().WriteAsync(new ProblemDetailsContext
        {
            HttpContext = http,
            ProblemDetails = new ProblemDetails { Status = status, Title = title, Detail = detail }
        }).AsTask();
    }
}
