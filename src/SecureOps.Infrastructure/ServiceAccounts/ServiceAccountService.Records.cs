using System.Security.Claims;
using System.Security.Cryptography;
using SecureOps.Domain.ServiceAccounts;
using SecureOps.Infrastructure.Access;
using SecureOps.Shared.Contracts.ServiceAccounts;

namespace SecureOps.Infrastructure.ServiceAccounts;

/// <summary>Evidence download payload.</summary>
public sealed record EvidenceDownload(string FileName, string ContentType, byte[] Content);

public sealed partial class ServiceAccountService
{
    private static readonly string[] _evidenceTypes = ["application/pdf", "image/png", "image/jpeg", "text/plain", "message/rfc822",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "application/vnd.openxmlformats-officedocument.wordprocessingml.document"];
    private static readonly string[] _findingStatuses = ["Open", "InReview", "Closed"];

    /// <summary>Reports a planned or performed action. Adding an action never closes a request (rule 9).</summary>
    public Task<SaResult<AccountDetail>> ReportActionAsync(ClaimsPrincipal principal, AccessOperationContext context, Guid accountId, ReportActionRequest request,
        CancellationToken cancellationToken) =>
        MutateAccountAsync(principal, context, accountId, ServiceAccountCapabilities.Work, p => p.CanWorkAnyRequest, (caller, detail) =>
        {
            // A participant reports only against its own open request.
            if (!detail.Permissions.Work && (request.RequestId is not { } own || !detail.Permissions.CanWorkRequest(own)))
            {
                return Task.FromResult(SaResult<Guid>.Fail(SaErrors.Forbidden, "requestId"));
            }

            if (!Enum.TryParse(request.ActionType, false, out ServiceAccountActionType type) || !Enum.TryParse(request.RecordKind, false, out ServiceAccountRecordKind kind))
            {
                return Task.FromResult(SaResult<Guid>.Fail(SaErrors.Invalid, "actionType"));
            }

            // Verification is a separate step on the same action; a new record can only be planned or performed.
            if (!Enum.TryParse(request.Result, false, out ServiceAccountActionResult result) || result == ServiceAccountActionResult.Verified)
            {
                return Task.FromResult(SaResult<Guid>.Fail(SaErrors.Invalid, "result"));
            }

            if (ServiceAccountRules.ValidateRecordKind(type, kind) is { } kindError)
            {
                return Task.FromResult(SaResult<Guid>.Fail(SaErrors.Invalid, kindError));
            }

            if (request.RequestId is { } requestId && detail.Requests.All(r => r.Id != requestId) || !ValidText(request.EvidenceNote, 2000)
                || References(request.References) is null)
            {
                return Task.FromResult(SaResult<Guid>.Fail(SaErrors.Invalid, "requestId"));
            }

            DateOnly? actual = request.ActualAt is { } at ? ReportCalendar.LocalDate(at) : request.ActualOn;
            return ServiceAccountRules.ValidateReport(result, actual, Today) is { } rule
                ? Task.FromResult(SaResult<Guid>.Fail(SaErrors.Invalid, rule))
                : repository!.ReportActionAsync(accountId, request, type, result, kind, caller.Actor, cancellationToken);
        }, cancellationToken);

    /// <summary>Moves a planned action to performed (same identity) or completes missing details.</summary>
    public Task<SaResult<AccountDetail>> UpdateActionAsync(ClaimsPrincipal principal, AccessOperationContext context, Guid actionId, UpdateActionRequest request,
        CancellationToken cancellationToken) =>
        EntityAsync(principal, context, "Action", actionId, ServiceAccountCapabilities.Work, p => p.CanWorkAnyRequest, (caller, detail, accountId) =>
        {
            // A participant completes only unverified actions linked to its own request.
            if (!detail.Permissions.Work && !ParticipantAction(detail, actionId))
            {
                return Task.FromResult(SaResult<Guid>.Fail(SaErrors.Forbidden, "requestId"));
            }

            ServiceAccountActionResult? result = request.Result is null ? null : Enum.TryParse(request.Result, false, out ServiceAccountActionResult r) ? r : null;
            ServiceAccountRecordKind? kind = request.RecordKind is null ? null : Enum.TryParse(request.RecordKind, false, out ServiceAccountRecordKind k) ? k : null;
            DateOnly? actual = request.ActualAt is { } at ? ReportCalendar.LocalDate(at) : request.ActualOn;
            if (request.Result is not null && result != ServiceAccountActionResult.Performed || request.RecordKind is not null && kind is null
                || !ValidText(request.EvidenceNote, 2000) || References(request.AddReferences) is null)
            {
                return Task.FromResult(SaResult<Guid>.Fail(SaErrors.Invalid, "result"));
            }

            if (kind is { } newKind && detail.Actions.FirstOrDefault(a => a.Id == actionId) is { } current
                && ServiceAccountRules.ValidateRecordKind(Enum.Parse<ServiceAccountActionType>(current.ActionType), newKind) is { } kindError)
            {
                return Task.FromResult(SaResult<Guid>.Fail(SaErrors.Invalid, kindError));
            }

            return ServiceAccountRules.ValidateReport(ServiceAccountActionResult.Performed, actual, Today) is { } rule
                ? Task.FromResult(SaResult<Guid>.Fail(SaErrors.Invalid, rule))
                : repository!.UpdateActionAsync(accountId, actionId, request, result, kind, caller.Actor, cancellationToken);
        }, cancellationToken);

    /// <summary>Verifies the same action (rule 4/6/11); the caller is recorded as verifier and evidence is required.</summary>
    public Task<SaResult<AccountDetail>> VerifyActionAsync(ClaimsPrincipal principal, AccessOperationContext context, Guid actionId, VerifyActionRequest request,
        CancellationToken cancellationToken) =>
        EntityAsync(principal, context, "Action", actionId, ServiceAccountCapabilities.Verify, p => p.Verify, (caller, detail, accountId) =>
        {
            ActionView? action = detail.Actions.FirstOrDefault(a => a.Id == actionId);
            if (action is null || !ValidText(request.VerificationNote, 2000))
            {
                return Task.FromResult(SaResult<Guid>.Fail(action is null ? SaErrors.NotFound : SaErrors.Invalid, "verificationNote"));
            }

            bool evidence = !string.IsNullOrWhiteSpace(request.VerificationNote) || !string.IsNullOrWhiteSpace(action.EvidenceNote)
                || request.EvidenceId is { } file && detail.Evidence.Any(e => e.Id == file);
            ActionFacts facts = Facts(action);
            if (ServiceAccountRules.ValidateVerification(facts, request.VerifiedOn, true, evidence, Today) is { } rule)
            {
                return Task.FromResult(SaResult<Guid>.Fail(SaErrors.Invalid, rule));
            }

            bool closure = ServiceAccountRules.IsVerifiedClosure(facts with
            {
                Result = ServiceAccountActionResult.Verified,
                VerifiedOn = request.VerifiedOn,
                HasVerifier = true,
                HasEvidence = true
            });
            return repository!.VerifyActionAsync(accountId, actionId, request, closure, caller.Actor, cancellationToken);
        }, cancellationToken);

    /// <summary>Voids an action with a reason; history is kept.</summary>
    public Task<SaResult<AccountDetail>> VoidActionAsync(ClaimsPrincipal principal, AccessOperationContext context, Guid actionId, VoidActionRequest request,
        CancellationToken cancellationToken) =>
        EntityAsync(principal, context, "Action", actionId, ServiceAccountCapabilities.Verify, p => p.Verify, (caller, _, accountId) =>
            ValidText(request.Reason, 1000, true)
                ? repository!.VoidActionAsync(accountId, actionId, request with { Reason = request.Reason.Trim() }, caller.Actor, cancellationToken)
                : Task.FromResult(SaResult<Guid>.Fail(SaErrors.Invalid, "reason")), cancellationToken);

    /// <summary>An unverified, non-void action linked to one of the participant's open requests.</summary>
    private static bool ParticipantAction(AccountDetail detail, Guid actionId) =>
        detail.Actions.FirstOrDefault(a => a.Id == actionId) is { RequestId: { } linked, Voided: false } action
        && action.Result != "Verified" && detail.Permissions.CanWorkRequest(linked);

    private static ActionFacts Facts(ActionView action) => new(Enum.Parse<ServiceAccountActionType>(action.ActionType), Enum.Parse<ServiceAccountActionResult>(action.Result),
        Enum.Parse<ServiceAccountRecordKind>(action.RecordKind), action.ActualOn, action.VerifiedOn, action.VerifiedByPerson is not null,
        !string.IsNullOrWhiteSpace(action.EvidenceNote), action.References.Any(r => r.Type == "OR"), action.Voided);

    /// <summary>Records one real mail linked to any number of in-scope accounts (rule 14).</summary>
    public Task<SaResult<CommunicationSaveResult>> CreateCommunicationAsync(ClaimsPrincipal principal, AccessOperationContext context,
        CreateCommunicationRequest request, CancellationToken cancellationToken) =>
        RunAsync(principal, context, ServiceAccountCapabilities.Work, async caller =>
        {
            if (!Enum.TryParse(request.Direction, false, out CommunicationDirection direction) || !Enum.TryParse(request.Kind, false, out CommunicationKind kind)
                || request.AccountIds.Count > 200 || !ValidText(request.Subject, 400) || !ValidText(request.Summary, 4000) || !ValidText(request.ProviderMessageId, 256)
                || request.Link is { } link && (!Uri.TryCreate(link, UriKind.Absolute, out Uri? uri) || uri.Scheme != Uri.UriSchemeHttps || link.Length > 400)
                || References(request.References) is null)
            {
                return SaResult<CommunicationSaveResult>.Fail(SaErrors.Invalid, "direction");
            }

            if (request.OccurredAt is { } at && ReportCalendar.LocalDate(at) > Today || request.OccurredOn is { } on && on > Today)
            {
                return SaResult<CommunicationSaveResult>.Fail(SaErrors.Invalid, ServiceAccountRules.Errors.DateInFuture);
            }

            foreach (Guid accountId in request.AccountIds.Distinct())
            {
                if (await repository!.AnchorAsync(accountId, cancellationToken) is not { } anchor || !caller.Scope.Covers(anchor.Anchor))
                {
                    return SaResult<CommunicationSaveResult>.Fail(SaErrors.NotFound, "accountIds");
                }
            }

            if (request.AccountIds.Count == 0 && !caller.Scope.HasOrganizationLevel && !caller.Scope.CoversTeam(request.ContactTeamId))
            {
                return SaResult<CommunicationSaveResult>.Fail(SaErrors.Forbidden, "contactTeamId");
            }

            SaResult<(Guid Id, bool Existing, int Added)> saved = await repository!.SaveCommunicationAsync(request, direction, kind, caller.Actor, cancellationToken);
            return saved.IsSuccess && await repository.CommunicationAsync(saved.Value.Id, cancellationToken) is { } view
                ? new CommunicationSaveResult(view with { Accounts = [.. view.Accounts.Where(a => request.AccountIds.Contains(a.Id) || caller.Scope.All)] },
                    saved.Value.Existing, saved.Value.Added)
                : SaResult<CommunicationSaveResult>.Fail(saved.ErrorCode ?? SaErrors.NotFound, saved.Field);
        }, cancellationToken);

    /// <summary>Records a technical finding; it never counts as work (rule 15).</summary>
    public Task<SaResult<AccountDetail>> CreateFindingAsync(ClaimsPrincipal principal, AccessOperationContext context, CreateFindingRequest request,
        CancellationToken cancellationToken) =>
        MutateAccountAsync(principal, context, request.AccountId, ServiceAccountCapabilities.Work, p => p.Work, (caller, detail) =>
            !Enum.TryParse<FindingScanResult>(request.ScanResult, false, out _) || !Enum.TryParse<FindingMatchResult>(request.MatchResult, false, out _)
                || !_findingStatuses.Contains(request.Status, StringComparer.Ordinal) || !ValidText(request.Server, 256) || !ValidText(request.ComponentName, 256)
                || !ValidText(request.CoverageWindow, 400) || !ValidText(request.EvidenceNote, 2000) || !ValidText(request.Notes, 2000)
                ? Task.FromResult(SaResult<Guid>.Fail(SaErrors.Invalid, "scanResult"))
                : repository!.CreateFindingAsync(request, caller.Actor, cancellationToken), cancellationToken);

    /// <summary>Changes a finding status.</summary>
    public Task<SaResult<AccountDetail>> UpdateFindingAsync(ClaimsPrincipal principal, AccessOperationContext context, Guid id, UpdateFindingRequest request,
        CancellationToken cancellationToken) =>
        EntityAsync(principal, context, "Finding", id, ServiceAccountCapabilities.Work, p => p.Work, (caller, _, accountId) =>
            !_findingStatuses.Contains(request.Status, StringComparer.Ordinal) || !ValidText(request.Notes, 2000)
                ? Task.FromResult(SaResult<Guid>.Fail(SaErrors.Invalid, "status"))
                : repository!.UpdateFindingAsync(accountId, id, request, caller.Actor, cancellationToken), cancellationToken);

    /// <summary>Proposes a handover (coordinator); proposal is not acceptance.</summary>
    public Task<SaResult<AccountDetail>> CreateHandoverAsync(ClaimsPrincipal principal, AccessOperationContext context, Guid accountId, CreateHandoverRequest request,
        CancellationToken cancellationToken) =>
        MutateAccountAsync(principal, context, accountId, ServiceAccountCapabilities.Assign, p => p.AssignTeam, (caller, _) =>
            !ValidText(request.Note, 1000) || !ValidText(request.CohortLabel, 200) || request.ProposedOn is { } on && on > Today
                ? Task.FromResult(SaResult<Guid>.Fail(SaErrors.Invalid, "note"))
                : repository!.CreateHandoverAsync(accountId, request, caller.Actor, cancellationToken), cancellationToken);

    /// <summary>Accepts or rejects a handover by the authorized target team with a real date and evidence note (rule 10).</summary>
    public Task<SaResult<AccountDetail>> DecideHandoverAsync(ClaimsPrincipal principal, AccessOperationContext context, Guid id, HandoverDecisionRequest request,
        CancellationToken cancellationToken) =>
        EntityAsync(principal, context, "Handover", id, ServiceAccountCapabilities.Assign, p => p.DecideHandover, (caller, detail, accountId) =>
        {
            HandoverView? handover = detail.Handovers.FirstOrDefault(h => h.Id == id);
            if (handover is null || request.Decision is not ("Accept" or "Reject") || !ValidText(request.Note, 1000, true) || request.DecidedOn > Today
                || handover.ProposedOn is { } proposed && request.DecidedOn < proposed)
            {
                return Task.FromResult(SaResult<Guid>.Fail(handover is null ? SaErrors.NotFound : SaErrors.Invalid, "decidedOn"));
            }

            return !caller.Scope.HasOrganizationLevel && !caller.Scope.CoversTeam(handover.TargetTeam.Id)
                ? Task.FromResult(SaResult<Guid>.Fail(SaErrors.Forbidden, "targetTeam"))
                : repository!.DecideHandoverAsync(accountId, id, request, request.Decision == "Accept", caller.Actor, cancellationToken);
        }, cancellationToken);

    /// <summary>Updates gMSA suitability/plan; a suitability decision needs a note and completion needs a real gMSA action.</summary>
    public Task<SaResult<AccountDetail>> UpdateTransitionAsync(ClaimsPrincipal principal, AccessOperationContext context, Guid id, TransitionUpdateRequest request,
        CancellationToken cancellationToken) =>
        EntityAsync(principal, context, "Transition", id, ServiceAccountCapabilities.Assign, p => p.AssignPerson || p.DecideHandover, (caller, _, accountId) =>
            !Enum.TryParse(request.Suitability, false, out GmsaSuitability suitability) || !ValidText(request.DecisionNote, 2000)
                || suitability is GmsaSuitability.Eligible or GmsaSuitability.Ineligible && string.IsNullOrWhiteSpace(request.DecisionNote)
                ? Task.FromResult(SaResult<Guid>.Fail(SaErrors.Invalid, "decisionNote"))
                : repository!.UpdateTransitionAsync(accountId, id, request, caller.Actor, cancellationToken), cancellationToken);

    /// <summary>Stores evidence for an in-scope entity (type and size bounded, hash recorded).</summary>
    public Task<SaResult<AccountDetail>> AddEvidenceAsync(ClaimsPrincipal principal, AccessOperationContext context, Guid accountId, string ownerType, Guid ownerId,
        string fileName, string contentType, byte[] content, string? label, CancellationToken cancellationToken) =>
        MutateAccountAsync(principal, context, accountId, ServiceAccountCapabilities.Work, p => p.UploadEvidence, (caller, detail) =>
        {
            // A participant attaches evidence only to its own requests and the actions linked to them.
            if (!detail.Permissions.Work && !(ownerType == "Request" && detail.Permissions.CanWorkRequest(ownerId)
                || ownerType == "Action" && ParticipantAction(detail, ownerId)))
            {
                return Task.FromResult(SaResult<Guid>.Fail(SaErrors.Forbidden, "ownerId"));
            }

            bool owned = ownerType switch
            {
                "Account" => ownerId == accountId,
                "Request" => detail.Requests.Any(r => r.Id == ownerId),
                "Action" => detail.Actions.Any(a => a.Id == ownerId),
                "Communication" => detail.Communications.Any(c => c.Id == ownerId),
                "Finding" => detail.Findings.Any(f => f.Id == ownerId),
                "Handover" => detail.Handovers.Any(h => h.Id == ownerId),
                _ => false
            };
            string safeName = SafeName(fileName);
            if (!owned || content.Length == 0 || content.Length > _options.MaxEvidenceBytes || !_evidenceTypes.Contains(contentType, StringComparer.OrdinalIgnoreCase)
                || !SignatureMatches(contentType, content) || !ValidText(label, 400))
            {
                return Task.FromResult(SaResult<Guid>.Fail(SaErrors.Invalid, owned ? "file" : "ownerId"));
            }

            string sha = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
            return repository!.AddEvidenceAsync(ownerType, ownerId, accountId, null, safeName, contentType.ToLowerInvariant(), content, sha, label, caller.Actor, cancellationToken);
        }, cancellationToken);

    /// <summary>Downloads evidence under the same scope policy; the download is audited.</summary>
    public Task<SaResult<EvidenceDownload>> EvidenceAsync(ClaimsPrincipal principal, AccessOperationContext context, Guid id, CancellationToken cancellationToken) =>
        RunAsync(principal, context, ServiceAccountCapabilities.View, async caller =>
        {
            EvidenceFile? file = await repository!.EvidenceAsync(id, cancellationToken);
            bool visible = file is not null && (file.AccountId is { } account
                ? await repository.AnchorAsync(account, cancellationToken) is { } anchor && caller.Scope.Covers(anchor.Anchor)
                : caller.Scope.CoversTeam(file.ScopeTeamId));
            if (!visible)
            {
                return SaResult<EvidenceDownload>.Fail(SaErrors.NotFound);
            }

            await repository.AuditReadAsync("EvidenceDownloaded", new { EvidenceId = id, file!.AccountId }, caller.Actor, cancellationToken);
            return new EvidenceDownload(file.FileName, file.ContentType, file.Content);
        }, cancellationToken);

    private static bool SignatureMatches(string contentType, byte[] content) => contentType.ToLowerInvariant() switch
    {
        "application/pdf" => content.AsSpan().StartsWith("%PDF-"u8),
        "image/png" => content.AsSpan().StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47 }),
        "image/jpeg" => content.AsSpan().StartsWith(new byte[] { 0xFF, 0xD8, 0xFF }),
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" or "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
            => Import.SpreadsheetReader.IsZip(content),
        _ => !content.Take(1024).Any(b => b == 0)
    };
}
