using System.Data.Common;
using System.Security.Claims;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SecureOps.Domain.Access;
using SecureOps.Domain.ServiceAccounts;
using SecureOps.Infrastructure.Access;
using SecureOps.Shared.Contracts.ServiceAccounts;

namespace SecureOps.Infrastructure.ServiceAccounts;

/// <summary>Resolved caller: approved application user, capabilities and server-side data scope.</summary>
/// <param name="User">Persisted application user.</param>
/// <param name="Scope">Resolved scope.</param>
/// <param name="Actor">Audit actor.</param>
public sealed record SaCaller(ApplicationUser User, ServiceAccountScope Scope, SaActor Actor)
{
    /// <summary>Capability check against persisted capabilities.</summary>
    public bool Can(string capability) => User.Capabilities.Contains(capability, StringComparer.Ordinal);
}

/// <summary>
/// Orchestrates the module. Every operation revalidates persisted approval, capability and scope; imported
/// people/teams never grant access and no second login store exists.
/// </summary>
public sealed partial class ServiceAccountService(SqlServiceAccountRepository? repository, IApplicationAccessService access,
    IOptions<ServiceAccountOptions> options, TimeProvider clock, ILogger<ServiceAccountService> logger)
{
    private readonly ServiceAccountOptions _options = options.Value;

    /// <summary>Current Europe/Istanbul business date.</summary>
    private DateOnly Today => ReportCalendar.LocalDate(clock.GetUtcNow());

    /// <summary>Caller module context for navigation and page composition.</summary>
    public Task<SaResult<ServiceAccountMe>> MeAsync(ClaimsPrincipal principal, AccessOperationContext context, CancellationToken cancellationToken) =>
        RunAsync(principal, context, ServiceAccountCapabilities.View, async caller =>
        {
            IReadOnlyList<SaRef> orgs = await repository!.OrganizationRefsAsync(caller.Scope.Organizations, cancellationToken);
            IReadOnlyList<SaRef> teams = await repository.TeamRefsAsync(caller.Scope.DirectTeams, cancellationToken);
            return new SaResult<ServiceAccountMe>(new ServiceAccountMe(true,
                [.. ServiceAccountCapabilities.All.Where(caller.Can)],
                caller.Scope.All ? "All" : caller.Scope.Organizations.Count > 0 ? "Organization" : caller.Scope.Teams.Count > 0 ? "Team" : "None",
                orgs, teams, !caller.Scope.IsEmpty));
        }, cancellationToken);

    private async Task<SaResult<T>> RunAsync<T>(ClaimsPrincipal principal, AccessOperationContext context, string capability,
        Func<SaCaller, Task<SaResult<T>>> operation, CancellationToken cancellationToken)
    {
        if (!_options.Enabled || repository is null)
        {
            return SaResult<T>.Fail(SaErrors.NotConfigured);
        }

        try
        {
            AccessServiceResult<EnsureAccessUserResult> current = await access.GetCurrentAsync(principal, context, cancellationToken);
            if (!current.IsSuccess || current.Value!.User is not { Status: AccessStatus.Approved } user || user.Id == Guid.Empty
                || !user.Capabilities.Contains(capability, StringComparer.Ordinal))
            {
                return SaResult<T>.Fail(SaErrors.Forbidden);
            }

            ScopeData data = await repository.LoadScopeDataAsync(user.Id, cancellationToken);
            var scope = ServiceAccountScope.Resolve(data.Grants, data.Organizations, data.Teams);
            return await operation(new SaCaller(user, scope, new SaActor(user.Id, context.CorrelationId)));
        }
        catch (SqlException exception)
        {
            logger.LogError("Service Accounts SQL persistence failed. Number={SqlNumber} State={SqlState} Class={SqlClass} CorrelationId={CorrelationId} Origin={Origin}",
                exception.Number, exception.State, exception.Class, context.CorrelationId, Origin(exception));
            return SaResult<T>.Fail(SaErrors.Unavailable);
        }
        catch (Exception exception) when (exception is DbException or IOException or InvalidOperationException or TimeoutException)
        {
            // Exception messages may contain parameters or file content; only the type and throwing method are logged.
            logger.LogError("Service Accounts persistence failed. FailureType: {FailureType} Origin={Origin} CorrelationId={CorrelationId}",
                exception.GetType().Name, Origin(exception), context.CorrelationId);
            return SaResult<T>.Fail(SaErrors.Unavailable);
        }
    }

    /// <summary>Throwing type and method only (no message, no values) so a failure can be located safely.</summary>
    private static string Origin(Exception exception) =>
        exception.TargetSite is { } site ? $"{site.DeclaringType?.Name}.{site.Name}" : "unknown";

    private static bool ValidText(string? value, int max, bool required = false) =>
        value is null ? !required : value.Trim().Length > 0 && value.Length <= max || !required && value.Length == 0;
}
