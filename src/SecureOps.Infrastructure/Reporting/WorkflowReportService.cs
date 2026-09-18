using System.Data.Common;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.SqlClient;
using SecureOps.Domain.Access;
using SecureOps.Infrastructure.Access;
using SecureOps.Shared.Auth;
using SecureOps.Shared.Contracts.Reporting;

namespace SecureOps.Infrastructure.Reporting;

/// <summary>Existing report permission plus each module's existing record scope, rechecked on every request.</summary>
public sealed class WorkflowReportService(IApplicationAccessService access, SqlWorkflowReportStore store,
    Announcements.OperationsDiagnostics? diagnostics = null)
{
    /// <summary>Captures a new local reporting cut; never initiates discovery or an external action.</summary>
    public Task<ManagementReportingResult<WorkflowReport>> CaptureAsync(ClaimsPrincipal principal, AccessOperationContext context,
        WorkflowReportRequest request, CancellationToken token) => RunAsync(principal, context, async user =>
        {
            if (!Valid(request))
            { return ManagementReportingResult<WorkflowReport>.Fail("ReportingValidationFailed"); }
            string scope = Scope(user);
            Shared.Contracts.Announcements.AnnouncementSourceReadiness? queue = diagnostics is null ? null : await diagnostics.QueueAsync(token);
            Guid id = await store.CaptureAsync(user, scope, request,
                user.Capabilities.Contains(Capabilities.InUseView), user.Capabilities.Contains(Capabilities.OperationalRecordsView),
                user.Capabilities.Contains(Capabilities.AnnouncementDrafts), context.CorrelationId, token,
                new("Worker", queue?.State ?? "Unknown", queue?.LastHeartbeat,
                    "Kuyruk / heartbeat gözlemi; kaynak işinin veya dış işlemin başarısı değildir"));
            WorkflowReport? report = await store.ReadAsync(id, user, scope, new(), context.CorrelationId, false, token);
            return report is null ? ManagementReportingResult<WorkflowReport>.Fail("ReportingSnapshotExpired") : ManagementReportingResult<WorkflowReport>.Success(report);
        }, token);

    /// <summary>Reads exactly the authorized retained cut, including export, without recapturing facts.</summary>
    public Task<ManagementReportingResult<WorkflowReport>> ReadAsync(ClaimsPrincipal principal, AccessOperationContext context,
        Guid id, WorkflowReportFilter filter, bool export, CancellationToken token) => RunAsync(principal, context, async user =>
        {
            if (!Valid(filter) || id == Guid.Empty)
            { return ManagementReportingResult<WorkflowReport>.Fail("ReportingValidationFailed"); }
            WorkflowReport? report = await store.ReadAsync(id, user, Scope(user), filter, context.CorrelationId, export, token);
            return report is null ? ManagementReportingResult<WorkflowReport>.Fail("ReportingSnapshotExpired") : ManagementReportingResult<WorkflowReport>.Success(report);
        }, token);

    /// <summary>Bounds interval/timezone without relying on the host timezone.</summary>
    public static bool Valid(WorkflowReportRequest request) => request.From < request.To && request.To <= DateTimeOffset.UtcNow
        && request.To - request.From <= TimeSpan.FromDays(92) && request.TimeZone is "UTC" or "UTC+03:00";
    /// <summary>Bounds filters and page arithmetic; identifiers are SQL parameters, not SQL fragments.</summary>
    public static bool Valid(WorkflowReportFilter filter) => filter.Page is >= 1 and <= 2000 && filter.PageSize is >= 1 and <= 100
        && filter.Module is null or "InUse" or "Sdm" or "Oco" && filter.Status?.Length is not > 64
        && filter.RecordType?.Length is not > 64 && filter.Metric?.Length is not > 64;
    private static string Scope(ApplicationUser user) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        string.Join("|", user.Capabilities.Order(StringComparer.Ordinal)))));

    private async Task<ManagementReportingResult<WorkflowReport>> RunAsync(ClaimsPrincipal principal, AccessOperationContext context,
        Func<ApplicationUser, Task<ManagementReportingResult<WorkflowReport>>> work, CancellationToken token)
    {
        try
        {
            AccessServiceResult<EnsureAccessUserResult> result = await access.GetCurrentAsync(principal, context, token);
            if (!result.IsSuccess || result.Value!.User.Status != AccessStatus.Approved
                || !result.Value.User.Capabilities.Contains(Capabilities.ManagementReportingView))
            { return ManagementReportingResult<WorkflowReport>.Fail("AccessDenied"); }
            if (!store.IsConfigured)
            { return ManagementReportingResult<WorkflowReport>.Fail("ReportingPersistenceNotConfigured"); }
            return await work(result.Value.User);
        }
        catch (SqlException ex) when (ex.Number == 51233) { return ManagementReportingResult<WorkflowReport>.Fail("ReportingLimitExceeded"); }
        catch (SqlException ex) when (ex.Number == 51234) { return ManagementReportingResult<WorkflowReport>.Fail("AccessDenied"); }
        catch (DbException) { return ManagementReportingResult<WorkflowReport>.Fail("ReportingUnavailable"); }
    }
}
