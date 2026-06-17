using FluentValidation;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.OpenApi.Models;
using SecureOps.Api.Middleware;
using SecureOps.Api.Security;
using SecureOps.Api.Validation;
using SecureOps.Infrastructure;
using SecureOps.Shared.Configuration;
using SecureOps.Shared.Contracts.Identity;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddAuthentication(NegotiateDefaults.AuthenticationScheme).AddNegotiate();
builder.Services.AddSecureOpsAuthorization(builder.Configuration, !builder.Environment.IsDevelopment());
builder.Services.AddRateLimiter(options =>
{
    int permitLimit = builder.Configuration.GetValue("IdentityLookup:RateLimit:PermitLimit", 10);
    int windowMinutes = builder.Configuration.GetValue("IdentityLookup:RateLimit:WindowMinutes", 1);

    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddFixedWindowLimiter(IdentityLookupRateLimits.Lookup, limiterOptions =>
    {
        limiterOptions.PermitLimit = permitLimit;
        limiterOptions.Window = TimeSpan.FromMinutes(windowMinutes);
        limiterOptions.QueueLimit = 0;
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

app.UseAuthentication();
app.UseMiddleware<AuthorizationDeniedAuditMiddleware>();
app.UseAuthorization();
app.UseRateLimiter();

app.MapControllers();
app.MapGet("/api/v1/health", () => Results.Ok(new { status = "Healthy" }))
    .WithName("Health")
    .WithOpenApi();
app.MapGet(
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

app.Run();

/// <summary>
/// API entry point exposed for integration testing.
/// </summary>
public partial class Program
{
}
