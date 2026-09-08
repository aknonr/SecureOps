using System.Data.Common;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SecureOps.Domain.Access;
using SecureOps.Infrastructure.Access;
using SecureOps.Infrastructure.Audit;
using SecureOps.Infrastructure.Commands;
using SecureOps.Infrastructure.OperationalRecords;
using SecureOps.Shared.Auth;
using SecureOps.Shared.Contracts.InUse;

namespace SecureOps.Infrastructure.InUse;

/// <summary>Authorized local In Use workflow. There is deliberately no external-write dependency.</summary>
public sealed class InUseService(IInUseRepository repository, IInUseSourceClient source,
    IApplicationAccessService access, IAccessRepository users, ICommandIdempotencyStore commands,
    ILogger<InUseService> logger)
{
    /// <summary>Queries only persisted records.</summary>
    public Task<InUseResult<InUsePage>> QueryAsync(ClaimsPrincipal principal, AccessOperationContext context,
        InUseQuery query, CancellationToken token) => RunAsync(principal, context, Capabilities.InUseView, async user =>
        {
            if (query.Page is < 1 or > 100000 || query.PageSize is < 1 or > 100 || query.Search?.Length > 100
                || query.View is not ("all" or "mine" or "unassigned") || query.Status is not (null or "Unreviewed" or "Draft" or "Stale"))
            { return InUseResult<InUsePage>.Fail("InUseInvalid"); }
            return new(await repository.QueryAsync(query, user.Id, token));
        }, token);

    /// <summary>Reads local detail without querying corporate data.</summary>
    public Task<InUseResult<InUseRecord>> GetAsync(ClaimsPrincipal principal, AccessOperationContext context,
        Guid id, CancellationToken token) => RunAsync(principal, context, Capabilities.InUseView, async _ =>
        {
            InUseRecord? record = await repository.GetAsync(id, token);
            return record is null ? InUseResult<InUseRecord>.Fail("InUseNotFound") : new(record);
        }, token);

    /// <summary>Minimal approved-reviewer picker; never resolves source display names into users.</summary>
    public Task<InUseResult<IReadOnlyList<InUseAssignee>>> AssigneesAsync(ClaimsPrincipal principal,
        AccessOperationContext context, CancellationToken token) => RunAsync<IReadOnlyList<InUseAssignee>>(principal, context,
        Capabilities.InUseAssign, async _ => new((await users.ListUsersAsync(token)).Where(Reviewer)
            .OrderBy(u => u.CorporateIdentity, StringComparer.Ordinal).Take(200)
            .Select(u => new InUseAssignee(u.Id, u.CorporateIdentity)).ToArray()), token);

    /// <summary>Refreshes a bounded independent scope, with durable command tracking and no deletion.</summary>
    public Task<InUseResult<InUseRefreshState>> RefreshAsync(ClaimsPrincipal principal, AccessOperationContext context,
        RefreshInUseRequest request, CancellationToken token) => RunAsync(principal, context, Capabilities.InUseRefresh, async user =>
        {
            if (request.CommandId == Guid.Empty)
            { return InUseResult<InUseRefreshState>.Fail("InUseInvalid"); }
            string key = request.CommandId.ToString("D");
            CommandBeginResult begin = await commands.TryBeginAsync("InUseRefresh", "4241:68", key,
                user.Id.ToString("D"), TimeSpan.FromMinutes(2), token);
            if (begin.Disposition == CommandBeginDisposition.Completed)
            { return new(await repository.StateAsync(token)); }
            if (begin.Disposition != CommandBeginDisposition.Acquired)
            { return InUseResult<InUseRefreshState>.Fail("InUseConflict"); }
            InUseRefreshState state = await repository.StateAsync(token);
            InUseBatch? batch = null;
            string? error = null;
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(TimeSpan.FromSeconds(45));
                batch = await source.DiscoverAsync(timeout.Token);
                if (!InUseState.Valid(batch))
                { throw new InvalidDataException("Invalid bounded In Use batch."); }
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested) { error = "SourceTimeout"; batch = null; }
            catch (Exception ex) when (ex is ExternalIntegrationException or HttpRequestException or IOException or InvalidDataException or JsonException or InvalidOperationException)
            { error = "SourceUnavailableOrMalformed"; batch = null; }
            bool saved = await repository.RefreshAsync(state.Version, batch, error,
                Audit(user, context, "Refresh", new { Count = batch?.Records.Count, batch?.Complete, Error = error ?? batch?.Issue }), token);
            if (!saved)
            {
                await commands.FailAsync("InUseRefresh", "4241:68", key, begin.ExecutionToken!.Value, "InUseConflict", token);
                return InUseResult<InUseRefreshState>.Fail("InUseConflict");
            }
            await commands.CompleteAsync("InUseRefresh", "4241:68", key, begin.ExecutionToken!.Value, token);
            return new(await repository.StateAsync(token));
        }, token);

    /// <summary>Manually assigns an approved reviewer with an audited reason and exact version.</summary>
    public Task<InUseResult<InUseRecord>> AssignAsync(ClaimsPrincipal principal, AccessOperationContext context,
        Guid id, AssignInUseRequest request, CancellationToken token) => RunAsync(principal, context, Capabilities.InUseAssign, async user =>
        {
            if (!Text(request.Reason, 500, required: true) || request.ExpectedVersion < 1 || request.AssigneeId == Guid.Empty)
            { return InUseResult<InUseRecord>.Fail("InUseInvalid"); }
            InUseRecord? old = await repository.GetAsync(id, token);
            if (old is null)
            { return InUseResult<InUseRecord>.Fail("InUseNotFound"); }
            if (old.Version != request.ExpectedVersion)
            { return InUseResult<InUseRecord>.Fail("InUseConflict"); }
            ApplicationUser? assignee = request.AssigneeId is Guid target ? await users.GetUserAsync(target, token) : null;
            if (request.AssigneeId.HasValue && (assignee is null || !Reviewer(assignee)))
            { return InUseResult<InUseRecord>.Fail("InUseAssigneeUnavailable"); }
            InUseRecord next = old with { Version = old.Version + 1, AssigneeId = assignee?.Id, AssigneeLabel = assignee?.CorporateIdentity };
            return await SaveAsync(next, request.ExpectedVersion, Audit(user, context, "Assigned",
                new { id, PreviousAssignee = old.AssigneeId, next.AssigneeId, request.Reason, next.Version }), token);
        }, token);

    /// <summary>Only the currently assigned reviewer can replace a local draft.</summary>
    public Task<InUseResult<InUseRecord>> SaveDraftAsync(ClaimsPrincipal principal, AccessOperationContext context,
        Guid id, SaveInUseDraftRequest request, CancellationToken token) => RunAsync(principal, context, Capabilities.InUseReview, async user =>
        {
            InUseRecord? old = await repository.GetAsync(id, token);
            if (old is null)
            { return InUseResult<InUseRecord>.Fail("InUseNotFound"); }
            if (old.AssigneeId != user.Id)
            { return InUseResult<InUseRecord>.Fail("InUseAssignmentRequired"); }
            if (request.SourceVersion != old.SourceVersion || request.ExpectedVersion != old.Version)
            { return InUseResult<InUseRecord>.Fail("InUseConflict"); }
            if (!ValidDraft(request, old))
            { return InUseResult<InUseRecord>.Fail("InUseInvalid"); }
            InUseRecord next = old with
            {
                Version = old.Version + 1,
                Draft = new(old.SourceVersion,
                request.Answers.OrderBy(a => a.ServerId, StringComparer.Ordinal).ThenBy(a => a.Check, StringComparer.Ordinal).ToArray(),
                request.Notes.Trim(), user.Id, DateTimeOffset.UtcNow)
            };
            return await SaveAsync(next, request.ExpectedVersion, Audit(user, context, "DraftSaved",
                new { id, next.Version, next.SourceVersion, AnswerCount = request.Answers.Count }), token);
        }, token);

    /// <summary>Prepares one version-bound text-only workbook and audits its hash before returning it.</summary>
    public Task<InUseResult<InUseReport>> ExportAsync(ClaimsPrincipal principal, AccessOperationContext context,
        Guid id, ExportInUseRequest request, CancellationToken token) => RunAsync(principal, context, Capabilities.InUseReview, async user =>
        {
            InUseRecord? record = await repository.GetAsync(id, token);
            if (record is null)
            { return InUseResult<InUseReport>.Fail("InUseNotFound"); }
            if (record.Version != request.ExpectedVersion || record.Draft is null || record.Draft.SourceVersion != record.SourceVersion)
            { return InUseResult<InUseReport>.Fail("InUseConflict"); }
            InUseReport report = InUseWorkbook.Create(record, user.Id, DateTimeOffset.UtcNow);
            bool saved = await repository.ExportAsync(id, request.ExpectedVersion, Audit(user, context, "ReportPrepared",
                new { id, record.Version, record.SourceVersion, report.Sha256 }), token);
            return saved ? new(report) : InUseResult<InUseReport>.Fail("InUseConflict");
        }, token);

    private async Task<InUseResult<InUseRecord>> SaveAsync(InUseRecord next, long expected, AuditEvent audit, CancellationToken token) =>
        await repository.SaveAsync(next, expected, audit, token) ? new(next) : InUseResult<InUseRecord>.Fail("InUseConflict");

    private async Task<InUseResult<T>> RunAsync<T>(ClaimsPrincipal principal, AccessOperationContext context, string capability,
        Func<ApplicationUser, Task<InUseResult<T>>> operation, CancellationToken token)
    {
        try
        {
            AccessServiceResult<EnsureAccessUserResult> current = await access.GetCurrentAsync(principal, context, token);
            if (!current.IsSuccess)
            { return InUseResult<T>.Fail(current.ErrorCode!); }
            ApplicationUser user = current.Value!.User;
            if (user.Id == Guid.Empty || user.Status != AccessStatus.Approved
                || !user.Capabilities.Contains(Capabilities.InUseView) || !user.Capabilities.Contains(capability))
            { return InUseResult<T>.Fail("AccessDenied"); }
            return await operation(user);
        }
        catch (Exception ex) when (ex is DbException or IOException or InvalidDataException or InvalidOperationException or JsonException)
        {
            logger.LogError("In Use local operation failed. FailureType: {FailureType}", ex.GetType().Name);
            return InUseResult<T>.Fail("PersistenceUnavailable");
        }
    }

    private static bool Reviewer(ApplicationUser user) => user.Status == AccessStatus.Approved
        && user.Capabilities.Contains(Capabilities.InUseView) && user.Capabilities.Contains(Capabilities.InUseReview);
    private static bool Text(string? value, int max, bool required = false) => value is not null && value.Length <= max
        && (!required || !string.IsNullOrWhiteSpace(value)) && !value.Any(c => char.IsControl(c) && c is not ('\r' or '\n' or '\t'));
    private static bool ValidDraft(SaveInUseDraftRequest request, InUseRecord record) =>
        Text(request.Notes, 2000) && request.Answers is not null && request.Answers.Count <= 900
        && request.Answers.All(a => a is not null)
        && request.Answers.Select(a => (a.ServerId, a.Check)).Distinct().Count() == request.Answers.Count
        && request.Answers.All(a => record.Source.Servers.Any(s => s.Id == a.ServerId)
            && InUseChecks.Codes.Contains(a.Check) && InUseChecks.Values.Contains(a.Value)
            && Text(a.Evidence, 500, required: a.Value != "Unknown"));
    private static AuditEvent Audit(ApplicationUser user, AccessOperationContext context, string action, object details) => new()
    { Actor = user.Id.ToString("D"), Action = "InUse" + action, CorrelationId = context.CorrelationId, Details = details };
}
