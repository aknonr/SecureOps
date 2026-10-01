using System.Security.Claims;
using SecureOps.Infrastructure.Access;
using SecureOps.Infrastructure.Commands;
using SecureOps.Shared.Auth;
using SecureOps.Shared.Contracts.InUse;

namespace SecureOps.Infrastructure.InUse;

public sealed partial class InUseService
{
    /// <summary>Trusted executor result journal, deliberately not exposed through HTTP. Blocked intents cannot advance.</summary>
    public Task<InUseResult<InUseRecord>> RecordCompletionOutcomeAsync(ClaimsPrincipal principal, AccessOperationContext context,
        Guid id, long expectedVersion, Guid commandId, string outcome, string? remoteId, CancellationToken token) =>
        RunAsync(principal, context, Capabilities.InUseReview, async user =>
        {
            InUseRecord? current = await repository.GetAsync(id, token);
            if (current is null)
            { return InUseResult<InUseRecord>.Fail("InUseNotFound"); }
            if (current.Version != expectedVersion || current.Completion is not { } intent
                || intent.CommandId != commandId || intent.ActorId != user.Id)
            { return InUseResult<InUseRecord>.Fail("InUseConflict"); }
            if (outcome.Length > 40 || remoteId is not null && !Text(remoteId, 200))
            { return InUseResult<InUseRecord>.Fail("InUseInvalid"); }
            InUseCompletion updated;
            try
            { updated = InUseCompletionTransitions.Apply(intent, outcome, remoteId); }
            catch (InvalidOperationException)
            { return new(null, "InUseConflict", "Bu tamamlama adımı engelli veya mutabakat bekliyor. Yazmayı yeniden denemeyin; kayıtlı sonucu inceleyin."); }
            return await SaveAsync(current with { Version = current.Version + 1, Completion = updated }, expectedVersion,
                Audit(user, context, "CompletionResult", new
                {
                    id,
                    commandId,
                    Previous = intent.Stage,
                    updated.Stage,
                    updated.ReportVersion,
                    updated.ReportSha256,
                    updated.AttachmentId,
                    updated.TaskId,
                    updated.FinalOrState
                }, id, expectedVersion, commandId, current.AssigneeId), token);
        }, token);

    /// <summary>Authorized stored overview. The existing transactional audit gates its release.</summary>
    public Task<InUseResult<InUseOverview>> OverviewAsync(ClaimsPrincipal principal, AccessOperationContext context, CancellationToken token) =>
        RunAsync<InUseOverview>(principal, context, Capabilities.ManagementReportingView,
            async user => new(await repository.OverviewAsync(Audit(user, context, "OverviewRead", new { Scope = "StoredOrs" }), token)), token);

    /// <summary>Records a local, blocked intent only. No external write implementation is reachable.</summary>
    public Task<InUseResult<InUseRecord>> ConfirmAsync(ClaimsPrincipal principal, AccessOperationContext context,
        Guid id, ConfirmInUseRequest request, CancellationToken token) => RunAsync(principal, context, Capabilities.InUseReview, async user =>
        {
            if (request.CommandId == Guid.Empty || request.ReportSha256?.Length != 64)
            { return InUseResult<InUseRecord>.Fail("InUseInvalid"); }
            InUseRecord? old = await repository.GetAsync(id, token);
            if (old is null)
            { return InUseResult<InUseRecord>.Fail("InUseNotFound"); }
            if (old.Completion is { } previous && previous.CommandId == request.CommandId)
            {
                return previous.ActorId == user.Id && previous.ReportVersion == request.ExpectedVersion && previous.ReportSha256 == request.ReportSha256
                    ? new(old) : InUseResult<InUseRecord>.Fail("InUseConflict");
            }
            if (old.Version != request.ExpectedVersion)
            { return InUseResult<InUseRecord>.Fail("InUseConflict"); }
            if (!InUseProgress.Ready(old))
            { return new(null, "InUseIncomplete", "Sunucu cevapları veya kaynak kanıtı eksik/eski. Taslağı doğrulayın; tamamlama yapılamaz."); }
            InUseReport? report = await (archive ?? throw new InvalidOperationException("Archive unavailable.")).AccessAsync(id, old.Version, null,
                r => Task.FromResult(r.SourceVersion == old.SourceVersion && r.SourceId == old.Source.Id && r.Sha256 == request.ReportSha256), token);
            if (report is null)
            { return InUseResult<InUseRecord>.Fail("InUseConflict"); }
            string key = request.CommandId.ToString("D"), target = id.ToString("D");
            CommandBeginResult begin = await commands.TryBeginAsync("InUseCompletionIntent", target, key, user.Id.ToString("D"), TimeSpan.FromMinutes(2), token);
            if (begin.Disposition != CommandBeginDisposition.Acquired)
            { return InUseResult<InUseRecord>.Fail("InUseConflict"); }
            InUseRecord next = old with
            {
                Version = old.Version + 1,
                Completion = new(request.CommandId, user.Id, report.Version, report.SourceVersion, report.Sha256, DateTimeOffset.UtcNow)
            };
            InUseResult<InUseRecord> saved = await SaveAsync(next, old.Version, Audit(user, context, "CompletionIntentBlocked",
                new { id, request.CommandId, report.Version, report.SourceVersion, report.Sha256, Stage = "Blocked" },
                id, old.Version, request.CommandId, old.AssigneeId), token);
            if (saved.Error is null)
            { await commands.CompleteAsync("InUseCompletionIntent", target, key, begin.ExecutionToken!.Value, token); }
            else
            { await commands.FailAsync("InUseCompletionIntent", target, key, begin.ExecutionToken!.Value, saved.Error, token); }
            return saved;
        }, token);
}
