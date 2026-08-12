using System.Threading.RateLimiting;
using FluentValidation;
using Microsoft.AspNetCore.Authentication;
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
using SecureOps.Infrastructure.Identity;
using SecureOps.Infrastructure.OperationalRecords;
using SecureOps.Shared.Auth;
using SecureOps.Shared.Configuration;
using SecureOps.Shared.Contracts.Api;
using SecureOps.Shared.Contracts.Audit;
using SecureOps.Shared.Contracts.Identity;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

AuditConfigurationValidator.Validate(builder.Configuration, builder.Environment.EnvironmentName);
IdentityLookupConfigurationValidator.Validate(builder.Configuration);
ReverseProxyConfiguration.Validate(builder.Configuration);
OperationalRecordConfigurationValidator.Validate(builder.Configuration);

bool demoAuthEnabled = DemoApiAuthentication.IsEnabled(
    builder.Environment.EnvironmentName,
    builder.Configuration);

builder.Services.Configure<DemoApiAuthOptions>(builder.Configuration.GetSection(DemoApiAuthOptions.SectionName));
builder.Services.Configure<SwaggerOptions>(builder.Configuration.GetSection(SwaggerOptions.SectionName));

AuthenticationBuilder authentication = builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = demoAuthEnabled
        ? DemoApiAuthentication.SchemeName
        : NegotiateDefaults.AuthenticationScheme;
});

if (demoAuthEnabled)
{
    authentication.AddScheme<AuthenticationSchemeOptions, DemoApiAuthenticationHandler>(
        DemoApiAuthentication.SchemeName,
        configureOptions: null);
}
else
{
    authentication.AddNegotiate();
}

builder.Services.AddSecureOpsAuthorization(builder.Configuration, !builder.Environment.IsDevelopment());
builder.Services.Configure<ForwardedHeadersOptions>(options => ReverseProxyConfiguration.Configure(options, builder.Configuration));
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
    if (demoAuthEnabled)
    {
        options.AddSecurityDefinition("DemoActor", new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.ApiKey,
            In = ParameterLocation.Header,
            Name = builder.Configuration["DemoAuth:HeaderName"] ?? "X-SecureOps-Demo-Actor",
            Description = "Demo/Test only. Enter an approved demo actor key; no actor is prefilled."
        });
        options.AddSecurityRequirement(new OpenApiSecurityRequirement
        {
            {
                new OpenApiSecurityScheme
                {
                    Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "DemoActor" }
                },
                Array.Empty<string>()
            }
        });
    }
});
builder.Services.AddSecureOpsInfrastructure(builder.Configuration);
if (builder.Configuration.GetValue("Audit:Queue:Enabled", true)
    && !string.Equals(builder.Configuration["Audit:Provider"], "InMemory", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddHostedService<AuditQueueHostedService>();
}

WebApplication app = builder.Build();

SwaggerOptions swaggerOptions = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<SwaggerOptions>>().Value;
bool swaggerEnabled = swaggerOptions.Enabled;
bool swaggerUiEnabled = swaggerEnabled && (app.Environment.IsDevelopment() || DemoApiAuthentication.IsAllowedEnvironment(app.Environment.EnvironmentName));

if (swaggerUiEnabled)
{
    app.UseSwaggerUI();
}

app.UseForwardedHeaders();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseAuthentication();
app.UseMiddleware<AccessDeniedProblemDetailsMiddleware>();
app.UseMiddleware<AuthorizationDeniedAuditMiddleware>();
app.UseAuthorization();
app.UseRateLimiter();
app.UseMiddleware<SafeExceptionHandlingMiddleware>();

if (swaggerEnabled)
{
    IEndpointConventionBuilder swaggerEndpoint = app.MapSwagger();
    if (app.Environment.IsDevelopment() || DemoApiAuthentication.IsAllowedEnvironment(app.Environment.EnvironmentName))
    {
        // The document must load before Swagger UI can collect its explicit DemoAuth header.
        // API operation endpoints remain protected by the fallback authorization policy.
        swaggerEndpoint.AllowAnonymous();
    }
    else
    {
        swaggerEndpoint.RequireAuthorization(Policies.AdminOnly);
    }
}

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
