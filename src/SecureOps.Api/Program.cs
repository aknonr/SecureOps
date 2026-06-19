using System.Threading.RateLimiting;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.OpenApi.Models;
using SecureOps.Api.Middleware;
using SecureOps.Api.Security;
using SecureOps.Api.Services;
using SecureOps.Api.Validation;
using SecureOps.Infrastructure;
using SecureOps.Infrastructure.Audit;
using SecureOps.Shared.Configuration;
using SecureOps.Shared.Contracts.Api;
using SecureOps.Shared.Contracts.Audit;
using SecureOps.Shared.Contracts.Identity;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

AuditConfigurationValidator.Validate(builder.Configuration, builder.Environment.EnvironmentName);
builder.Services.AddAuthentication(NegotiateDefaults.AuthenticationScheme).AddNegotiate();
builder.Services.AddSecureOpsAuthorization(builder.Configuration, !builder.Environment.IsDevelopment());
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});
builder.Services.AddRateLimiter(options =>
{
    int permitLimit = builder.Configuration.GetValue("IdentityLookup:RateLimit:PermitLimit", 10);
    int windowMinutes = builder.Configuration.GetValue("IdentityLookup:RateLimit:WindowMinutes", 1);

    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        await context.HttpContext.Response.WriteAsJsonAsync(
            new ApiErrorResponse(
                "RateLimitExceeded",
                "Too many identity lookup requests. Try again later.",
                context.HttpContext.TraceIdentifier),
            cancellationToken);
    };
    options.AddPolicy(IdentityLookupRateLimits.Lookup, httpContext =>
    {
        return RateLimitPartition.GetFixedWindowLimiter(
            IdentityLookupRateLimits.GetPartitionKey(httpContext),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitLimit,
                Window = TimeSpan.FromMinutes(windowMinutes),
                QueueLimit = 0
            });
    });
});

builder.Services.AddControllers();
builder.Services.AddScoped<IValidator<IdentityLookupRequest>, IdentityLookupRequestValidator>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "SecureOps API",
        Version = "v1",
        Description = "Internal SecureOps backend API."
    });
    options.AddSecurityDefinition("WindowsAuth", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "negotiate",
        Description = "Windows Integrated Authentication / Negotiate. Non-development environments require authentication for Swagger and API endpoints."
    });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "WindowsAuth"
                }
            },
            Array.Empty<string>()
        }
    });
});
builder.Services.AddSecureOpsInfrastructure(builder.Configuration);
if (builder.Configuration.GetValue("Audit:Queue:Enabled", true)
    && !string.Equals(builder.Configuration["Audit:Provider"], "InMemory", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddHostedService<AuditQueueHostedService>();
}

WebApplication app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    app.MapSwagger().RequireAuthorization();
}

app.UseForwardedHeaders();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseAuthentication();
app.UseMiddleware<AuthorizationDeniedAuditMiddleware>();
app.UseAuthorization();
app.UseRateLimiter();

app.MapControllers();
RouteHandlerBuilder healthEndpoint = app.MapGet("/api/v1/health", () => Results.Ok(new { status = "Healthy" }))
    .WithName("Health")
    .WithOpenApi();
RouteHandlerBuilder auditStoreHealthEndpoint = app.MapGet(
        "/api/v1/health/audit-store",
        (AuditHealthReporter reporter) => Results.Ok(reporter.GetHealth()))
    .WithName("AuditStoreHealth")
    .WithOpenApi();
RouteHandlerBuilder identityProviderHealthEndpoint = app.MapGet(
        "/api/v1/health/identity-provider",
        (Microsoft.Extensions.Options.IOptions<IdentityLookupOptions> options) =>
        {
            IdentityLookupOptions identityOptions = options.Value;
            return Results.Ok(new IdentityProviderHealthResponse(
                "Configured",
                identityOptions.Provider,
                string.Equals(identityOptions.Provider, "ActiveDirectory", StringComparison.OrdinalIgnoreCase)));
        })
    .WithName("IdentityProviderHealth")
    .WithOpenApi();

if (!app.Environment.IsDevelopment())
{
    healthEndpoint.RequireAuthorization();
    auditStoreHealthEndpoint.RequireAuthorization();
    identityProviderHealthEndpoint.RequireAuthorization();
}

app.Run();

/// <summary>
/// API entry point exposed for integration testing.
/// </summary>
public partial class Program
{
}
