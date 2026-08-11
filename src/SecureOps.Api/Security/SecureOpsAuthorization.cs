using Microsoft.AspNetCore.Authorization;
using SecureOps.Shared.Auth;

namespace SecureOps.Api.Security;

/// <summary>
/// Central authorization policy registration for the SecureOps API.
/// </summary>
public static class SecureOpsAuthorization
{
    /// <summary>
    /// Adds SecureOps role policies.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="configuration">Application configuration.</param>
    /// <param name="requireAuthenticatedFallback">Whether every endpoint requires authentication by default.</param>
    /// <returns>The same service collection.</returns>
    public static IServiceCollection AddSecureOpsAuthorization(
        this IServiceCollection services,
        IConfiguration configuration,
        bool requireAuthenticatedFallback)
    {
        services.AddAuthorization(options =>
        {
            string[] operatorGroups = GetGroups(configuration, "Operators", "Leads", "Admins");
            string[] teamLeadGroups = GetGroups(configuration, "Leads", "Admins");
            string[] adminGroups = GetGroups(configuration, "Admins");
            string[] auditorGroups = GetGroups(configuration, "Auditors");
            string[] canViewAuditGroups = GetGroups(configuration, "Auditors", "Admins");

            options.AddPolicy(Policies.OperatorOrAbove, policy => policy.RequireAssertion(ctx =>
                ctx.User.Identity?.IsAuthenticated == true && operatorGroups.Any(ctx.User.IsInRole)));
            options.AddPolicy(Policies.TeamLeadOrAbove, policy => policy.RequireAssertion(ctx =>
                ctx.User.Identity?.IsAuthenticated == true && teamLeadGroups.Any(ctx.User.IsInRole)));
            options.AddPolicy(Policies.AdminOnly, policy => policy.RequireAssertion(ctx =>
                ctx.User.Identity?.IsAuthenticated == true && adminGroups.Any(ctx.User.IsInRole)));
            options.AddPolicy(Policies.AuditorOnly, policy => policy.RequireAssertion(ctx =>
                ctx.User.Identity?.IsAuthenticated == true && auditorGroups.Any(ctx.User.IsInRole)));
            options.AddPolicy(Policies.CanViewAudit, policy => policy.RequireAssertion(ctx =>
                ctx.User.Identity?.IsAuthenticated == true && canViewAuditGroups.Any(ctx.User.IsInRole)));
            options.AddPolicy(Policies.CanTriggerDiagnostic, policy => policy.RequireAssertion(ctx =>
                ctx.User.Identity?.IsAuthenticated == true && teamLeadGroups.Any(ctx.User.IsInRole)));
            options.AddPolicy(Policies.CanIdentityLookup, policy => policy.RequireAssertion(ctx =>
                ctx.User.Identity?.IsAuthenticated == true && teamLeadGroups.Any(ctx.User.IsInRole)));
            options.AddPolicy(Policies.CanBulkIdentityLookup, policy => policy.RequireAssertion(ctx =>
                ctx.User.Identity?.IsAuthenticated == true && teamLeadGroups.Any(ctx.User.IsInRole)));
            options.AddPolicy(Policies.CanTeamView, policy => policy.RequireAssertion(ctx =>
                ctx.User.Identity?.IsAuthenticated == true && operatorGroups.Any(ctx.User.IsInRole)));
            options.AddPolicy(Policies.CanAccessAdministration, policy => policy.RequireAssertion(ctx =>
                ctx.User.Identity?.IsAuthenticated == true && adminGroups.Any(ctx.User.IsInRole)));
            options.AddPolicy(Policies.CanSystemDiagnostics, policy => policy.RequireAssertion(ctx =>
                ctx.User.Identity?.IsAuthenticated == true && teamLeadGroups.Any(ctx.User.IsInRole)));

            if (requireAuthenticatedFallback)
            {
                options.FallbackPolicy = new AuthorizationPolicyBuilder()
                    .RequireAuthenticatedUser()
                    .Build();
            }
        });

        return services;
    }

    private static string[] GetGroups(IConfiguration configuration, params string[] roleCodes)
    {
        return roleCodes
            .Select(role => configuration[$"Rbac:{role}Group"] ?? $"CONTOSO\\SecureOps-{role}")
            .ToArray();
    }
}
