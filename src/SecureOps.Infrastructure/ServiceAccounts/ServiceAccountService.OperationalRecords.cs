using System.Security.Claims;
using System.Text.RegularExpressions;
using SecureOps.Domain.ServiceAccounts;
using SecureOps.Infrastructure.Access;
using SecureOps.Infrastructure.ItsmRecords;
using SecureOps.Infrastructure.ItsmRequests;
using SecureOps.Shared.Contracts.ServiceAccounts;

namespace SecureOps.Infrastructure.ServiceAccounts;

/// <summary>
/// The ITSM ports the OR flow uses (ADR-0030 creation, ADR-0031 lookup, ADR-0032 tracking and user match). Registered only by
/// hosts that compose them; without them every OR command answers "creation disabled" and nothing is written.
/// </summary>
/// <param name="Creator">OR creation provider (Disabled by default; Fake in synthetic environments; no real adapter exists).</param>
/// <param name="Users">Exact user match of the signed-in user.</param>
/// <param name="Lookup">Read-only record lookup by number.</param>
/// <param name="Tracking">Read-only OR tracking by source id.</param>
/// <param name="DispatchBound">Upper bound of one dispatch, independent of the browser request.</param>
public sealed record ServiceAccountOperationalRecordPorts(IItsmRequestCreator Creator, IItsmUserMapping Users, IItsmRecordLookup Lookup,
    IItsmRecordTracking Tracking, TimeSpan DispatchBound);

/// <summary>A validated preview and, when it may be sent, exactly what a confirmation stores and sends.</summary>
internal sealed record OperationalRecordDraftPlan(OperationalRecordPreview Preview, OperationalRecordReservation? Reservation, ItsmRequestDraft? Draft);

public sealed partial class ServiceAccountService
{
    // Opening an OR from an approved gMSA plan (ADR-0030, PROPOSED; OR-2 with a Fake adapter, migration 035). The service builds
    // the preview from the plan, the approved preview's servers and the caller's ITSM user (only Matched with a location may
    // send); the confirmation re-derives everything and refuses any difference from the previewed fingerprint. The repository
    // decides scope, basis and state again with the plan row locked. Nothing here contacts a managed server.

    /// <summary>The plan's OR panel: every account's attempts and what the caller may do now.</summary>
    public Task<SaResult<PlanOperationalRecordsView>> OperationalRecordsAsync(ClaimsPrincipal principal, AccessOperationContext context, Guid id,
        CancellationToken cancellationToken) =>
        RecordsAsync(principal, context, ServiceAccountCapabilities.View, async caller =>
            await VisiblePlanAsync(caller, id, cancellationToken) is { } plan
                ? RecordsView(caller, plan, await repository!.OperationalRecordSubmissionsAsync(id, cancellationToken))
                : SaResult<PlanOperationalRecordsView>.Fail(SaErrors.NotFound), cancellationToken);

    /// <summary>What a confirmation would send for one account (writes nothing, audits nothing).</summary>
    public Task<SaResult<OperationalRecordPreview>> PreviewOperationalRecordAsync(ClaimsPrincipal principal, AccessOperationContext context, Guid id,
        Guid accountId, OperationalRecordPreviewRequest request, CancellationToken cancellationToken) =>
        RecordsAsync(principal, context, ServiceAccountCapabilities.Work, async caller =>
        {
            SaResult<OperationalRecordDraftPlan> planned = await DraftOperationalRecordAsync(caller, id, accountId, request.ShortDescription, request.Description,
                request.Environment, cancellationToken);
            return planned.IsSuccess ? planned.Value!.Preview : SaResult<OperationalRecordPreview>.Fail(planned.ErrorCode!, planned.Field);
        }, cancellationToken);

    /// <summary>
    /// Sends the previewed OR once. A known idempotency key answers first (same fingerprint: the stored result, replayed; other:
    /// 409). Then the preview is rebuilt and must be sendable and produce the same fingerprint (409 <c>orPreviewStale</c>).
    /// </summary>
    public Task<SaResult<OperationalRecordSubmitResult>> SubmitOperationalRecordAsync(ClaimsPrincipal principal, AccessOperationContext context, Guid id,
        Guid accountId, OperationalRecordSubmitRequest request, CancellationToken cancellationToken) =>
        RecordsAsync(principal, context, ServiceAccountCapabilities.Work, async caller =>
        {
            if (request.IdempotencyKey is not { } key || key == Guid.Empty)
            {
                return SaResult<OperationalRecordSubmitResult>.Fail(SaErrors.IdempotencyKey, "idempotencyKey");
            }

            if (request.Fingerprint is null || !Regex.IsMatch(request.Fingerprint, "^[0-9a-f]{64}$", RegexOptions.None, TimeSpan.FromMilliseconds(50)))
            {
                return SaResult<OperationalRecordSubmitResult>.Fail(SaErrors.Invalid, "fingerprint");
            }

            if (await VisiblePlanAsync(caller, id, cancellationToken) is not { } visible || visible.Accounts.All(a => a.View.AccountId != accountId))
            {
                return SaResult<OperationalRecordSubmitResult>.Fail(SaErrors.NotFound);
            }

            // A replay is answered before revalidation: the first confirmation already holds the account's live submission.
            if (await repository!.OperationalRecordByKeyAsync(accountId, key, cancellationToken) is { } stored)
            {
                return stored.PlanId == id && string.Equals(stored.Fingerprint, request.Fingerprint, StringComparison.Ordinal)
                    ? new OperationalRecordSubmitResult(SubmissionView(stored), true)
                    : SaResult<OperationalRecordSubmitResult>.Fail(SaErrors.IdempotencyKeyReused, "idempotencyKey");
            }

            SaResult<OperationalRecordDraftPlan> planned = await DraftOperationalRecordAsync(caller, id, accountId, request.ShortDescription, request.Description,
                request.Environment, cancellationToken);
            if (!planned.IsSuccess)
            {
                return SaResult<OperationalRecordSubmitResult>.Fail(planned.ErrorCode!, planned.Field);
            }

            OperationalRecordDraftPlan draft = planned.Value!;
            if (draft.Reservation is null || draft.Draft is null)
            {
                return draft.Preview.BlockedReason == "environmentRequired"
                    ? SaResult<OperationalRecordSubmitResult>.Fail(SaErrors.Invalid, "environment")
                    : SaResult<OperationalRecordSubmitResult>.Fail(SaErrors.ChangePlanState, draft.Preview.BlockedReason);
            }

            if (!string.Equals(draft.Preview.Fingerprint, request.Fingerprint, StringComparison.Ordinal))
            {
                return SaResult<OperationalRecordSubmitResult>.Fail(SaErrors.ChangePlanState, "orPreviewStale");
            }

            OperationalRecordFlow flow = new(repository, operationalRecords!.Creator, operationalRecords.DispatchBound, logger);
            SaResult<(OperationalRecordSubmissionRecord Submission, bool Replayed)> sent = await flow.SubmitAsync(draft.Reservation with { IdempotencyKey = key },
                draft.Draft, PlanCaller(caller), cancellationToken);
            return sent.IsSuccess
                ? new OperationalRecordSubmitResult(SubmissionView(sent.Value.Submission), sent.Value.Replayed)
                : SaResult<OperationalRecordSubmitResult>.Fail(sent.ErrorCode!, sent.Field);
        }, cancellationToken);

    /// <summary>Looks up an OR number a person found in the ITSM for the account's unresolved submission (writes nothing).</summary>
    public Task<SaResult<OperationalRecordCandidate>> OperationalRecordCandidateAsync(ClaimsPrincipal principal, AccessOperationContext context, Guid id,
        Guid accountId, OperationalRecordCandidateRequest request, CancellationToken cancellationToken) =>
        RecordsAsync(principal, context, ServiceAccountCapabilities.Work, async caller =>
        {
            if (ItsmRecordNumber.Normalize(ItsmRecordKind.Or, request.Number) is not { } number)
            {
                return SaResult<OperationalRecordCandidate>.Fail(SaErrors.Invalid, "number");
            }

            if (await ReconcilableAsync(caller, id, accountId, cancellationToken) is { IsSuccess: false } refused)
            {
                return SaResult<OperationalRecordCandidate>.Fail(refused.ErrorCode!, refused.Field);
            }

            OperationalRecordSubmissionRecord? current = (await repository!.OperationalRecordSubmissionsAsync(id, cancellationToken))
                .FirstOrDefault(s => s.AccountId == accountId);
            ItsmRecordLookupResult lookup = await operationalRecords!.Lookup.LookupAsync(ItsmRecordKind.Or, number, cancellationToken);
            OperationalRecordTrackingView? tracking = lookup.Status == ItsmRecordLookupStatus.Found
                ? TrackingView(number, await operationalRecords.Tracking.GetOrTrackingAsync(lookup.Record!.SourceId, cancellationToken))
                : null;
            bool? titleMatches = lookup.Record is { } record && current is not null
                ? string.Equals(record.Title.Trim(), current.ShortDescription, StringComparison.OrdinalIgnoreCase)
                : null;
            return new OperationalRecordCandidate(number, lookup.Status.ToString(), LookupLabel(lookup.Status), Reason(lookup.Reason), lookup.Record?.Title,
                titleMatches, tracking, lookup.Provider, IsSynthetic(lookup.Provider), current is not null && OperationalRecordRules.CanLink(current.State));
        }, cancellationToken);

    /// <summary>
    /// Links an OR number to the account's unresolved submission. The server looks the number up again: Found records the link
    /// as verified, anything else as not verified (never as absent).
    /// </summary>
    public Task<SaResult<PlanOperationalRecordsView>> LinkOperationalRecordAsync(ClaimsPrincipal principal, AccessOperationContext context, Guid id,
        Guid accountId, OperationalRecordLinkRequest request, CancellationToken cancellationToken) =>
        RecordsAsync(principal, context, ServiceAccountCapabilities.Work, async caller =>
        {
            if (ItsmRecordNumber.Normalize(ItsmRecordKind.Or, request.Number) is not { } number)
            {
                return SaResult<PlanOperationalRecordsView>.Fail(SaErrors.Invalid, "number");
            }

            if (ServiceAccountText.Clean(request.Reason) is not { Length: <= OperationalRecordRules.MaxReason } reason)
            {
                return SaResult<PlanOperationalRecordsView>.Fail(SaErrors.Invalid, "reason");
            }

            if (await ReconcilableAsync(caller, id, accountId, cancellationToken) is { IsSuccess: false } refused)
            {
                return SaResult<PlanOperationalRecordsView>.Fail(refused.ErrorCode!, refused.Field);
            }

            ItsmRecordLookupResult lookup = await operationalRecords!.Lookup.LookupAsync(ItsmRecordKind.Or, number, cancellationToken);
            bool verified = lookup.Status == ItsmRecordLookupStatus.Found;
            return await AfterReconcileAsync(caller, id, await repository!.ReconcileOperationalRecordAsync(id, accountId, request.SubmissionId, number,
                verified ? lookup.Record!.SourceId : null, verified, reason, PlanCaller(caller), operationalRecords.DispatchBound, cancellationToken),
                cancellationToken);
        }, cancellationToken);

    /// <summary>Records a person's statement that no OR was created for the account's unresolved submission.</summary>
    public Task<SaResult<PlanOperationalRecordsView>> ConfirmOperationalRecordNotCreatedAsync(ClaimsPrincipal principal, AccessOperationContext context, Guid id,
        Guid accountId, OperationalRecordNotCreatedRequest request, CancellationToken cancellationToken) =>
        RecordsAsync(principal, context, ServiceAccountCapabilities.Work, async caller =>
        {
            if (ServiceAccountText.Clean(request.Reason) is not { Length: <= OperationalRecordRules.MaxReason } reason)
            {
                return SaResult<PlanOperationalRecordsView>.Fail(SaErrors.Invalid, "reason");
            }

            if (await ReconcilableAsync(caller, id, accountId, cancellationToken) is { IsSuccess: false } refused)
            {
                return SaResult<PlanOperationalRecordsView>.Fail(refused.ErrorCode!, refused.Field);
            }

            return await AfterReconcileAsync(caller, id, await repository!.ReconcileOperationalRecordAsync(id, accountId, request.SubmissionId, null, null, false,
                reason, PlanCaller(caller), operationalRecords!.DispatchBound, cancellationToken), cancellationToken);
        }, cancellationToken);

    /// <summary>Live, read-only tracking of the account's OR; not stored, not audited per call (ADR-0030 §6, §8).</summary>
    public Task<SaResult<OperationalRecordTrackingView>> OperationalRecordTrackingAsync(ClaimsPrincipal principal, AccessOperationContext context, Guid id,
        Guid accountId, CancellationToken cancellationToken) =>
        RecordsAsync(principal, context, ServiceAccountCapabilities.View, async caller =>
        {
            if (await VisiblePlanAsync(caller, id, cancellationToken) is not { } plan || plan.Accounts.All(a => a.View.AccountId != accountId))
            {
                return SaResult<OperationalRecordTrackingView>.Fail(SaErrors.NotFound);
            }

            OperationalRecordSubmissionRecord? record = (await repository!.OperationalRecordSubmissionsAsync(id, cancellationToken))
                .FirstOrDefault(s => s.AccountId == accountId && s.State is OperationalRecordState.Created or OperationalRecordState.PartiallyCreated
                    or OperationalRecordState.Linked);
            if (record is null)
            {
                return SaResult<OperationalRecordTrackingView>.Fail(SaErrors.ChangePlanState, "noRecord");
            }

            if (operationalRecords is null)
            {
                return SaResult<OperationalRecordTrackingView>.Fail(SaErrors.OperationalRecordCreationDisabled);
            }

            string? sourceId = record.ExternalId;
            if (sourceId is null && record.ExternalNumber is { } number)
            {
                ItsmRecordLookupResult lookup = await operationalRecords.Lookup.LookupAsync(ItsmRecordKind.Or, number, cancellationToken);
                if (lookup.Status != ItsmRecordLookupStatus.Found)
                {
                    return TrackingView(number, ItsmRecordTrackingResult.Unknown(null, lookup.Status == ItsmRecordLookupStatus.Unknown ? lookup.Reason
                        : ItsmRecordLookupReason.IncompleteResponse, lookup.Provider), lookup.Status);
                }

                sourceId = lookup.Record!.SourceId;
            }

            return TrackingView(record.ExternalNumber, await operationalRecords.Tracking.GetOrTrackingAsync(sourceId, cancellationToken));
        }, cancellationToken);

    /// <summary>Capability and 033 checks (PlansAsync), then "not installed" while migration 035 is missing.</summary>
    private Task<SaResult<T>> RecordsAsync<T>(ClaimsPrincipal principal, AccessOperationContext context, string capability,
        Func<SaCaller, Task<SaResult<T>>> operation, CancellationToken cancellationToken) =>
        PlansAsync(principal, context, capability, async caller =>
            await repository!.OperationalRecordsInstalledAsync(cancellationToken) ? await operation(caller) : SaResult<T>.Fail(SaErrors.OperationalRecordsNotInstalled),
            cancellationToken);

    /// <summary>
    /// Builds the preview for one account. Refusals that make a preview meaningless are errors (creation disabled 503, plan or
    /// account outside scope 404, no basis 403, plan not approved 409, invalid text 400); a preview that cannot be sent
    /// (submission exists, requester not matched or without a location, no environment yet) comes back with its reason.
    /// </summary>
    private async Task<SaResult<OperationalRecordDraftPlan>> DraftOperationalRecordAsync(SaCaller caller, Guid id, Guid accountId, string? shortDescription,
        string? description, string? environment, CancellationToken cancellationToken)
    {
        if (operationalRecords is not { Creator.IsEnabled: true } ports)
        {
            return SaResult<OperationalRecordDraftPlan>.Fail(SaErrors.OperationalRecordCreationDisabled);
        }

        if (await VisiblePlanAsync(caller, id, cancellationToken) is not { } plan
            || plan.Accounts.FirstOrDefault(a => a.View.AccountId == accountId) is not { } account)
        {
            return SaResult<OperationalRecordDraftPlan>.Fail(SaErrors.NotFound);
        }

        if (!caller.Can(ServiceAccountCapabilities.Work) || plan.Accounts.Any(a => !SqlServiceAccountRepository.PlanResponsible(caller.Scope, a.Anchor)))
        {
            return SaResult<OperationalRecordDraftPlan>.Fail(SaErrors.Forbidden, "scope");
        }

        if (plan.Status is not (ChangePlanStatus.Approved or ChangePlanStatus.InProgress) || plan.Approval is null)
        {
            return SaResult<OperationalRecordDraftPlan>.Fail(SaErrors.ChangePlanState, ChangePlanRules.IsOpen(plan.Status) ? "planNotApproved" : "planClosed");
        }

        if (await repository!.OperationalRecordContextAsync(id, accountId, cancellationToken) is not { } planContext)
        {
            return SaResult<OperationalRecordDraftPlan>.Fail(SaErrors.NotFound);
        }

        string gmsa = account.View.TargetGmsaName;
        int shortMax = Math.Clamp(_options.OperationalRecords.ShortDescriptionMax, 1, OperationalRecordRules.MaxShortDescriptionColumn);
        string reference = OperationalRecordRules.PlanReference(id, accountId);
        string title = OperationalRecordRules.NormalizeText(shortDescription ?? OperationalRecordRules.DefaultShortDescription(gmsa));
        string body = OperationalRecordRules.NormalizeText(description ?? OperationalRecordRules.DefaultDescription(gmsa, planContext.Servers));
        if (OperationalRecordRules.TextError(title, body, reference, shortMax) is { } field)
        {
            return SaResult<OperationalRecordDraftPlan>.Fail(SaErrors.Invalid, field);
        }

        string[] environments = [.. _options.OperationalRecords.Environments.Where(e => !string.IsNullOrWhiteSpace(e)).Select(e => e.Trim())];
        string? chosen = null;
        if (!string.IsNullOrWhiteSpace(environment))
        {
            chosen = environments.FirstOrDefault(e => string.Equals(e, environment.Trim(), StringComparison.OrdinalIgnoreCase));
            if (chosen is null)
            {
                return SaResult<OperationalRecordDraftPlan>.Fail(SaErrors.Invalid, "environment");
            }
        }

        OperationalRecordRequester requester = await OperationalRecordRequester.ResolveAsync(ports.Users, caller.User.Uid, caller.User.LoginName, cancellationToken);
        bool exists = (await repository.OperationalRecordSubmissionsAsync(id, cancellationToken))
            .Any(s => s.AccountId == accountId && OperationalRecordRules.BlocksNewSubmission(s.State));
        string? blocked = exists ? "submissionExists" : requester.BlockedReason ?? (chosen is null ? "environmentRequired" : null);
        string sent = OperationalRecordRules.ComposeDescription(body, reference);
        string? fingerprint = blocked is null
            ? OperationalRecordRules.Fingerprint(new OperationalRecordFingerprintInput(id, accountId, planContext.PreviewVersion, planContext.PreviewSha256, gmsa,
                planContext.Servers, ports.Creator.Provider, requester.UserId!, requester.HomeLocationId, chosen!, title, sent))
            : null;
        OperationalRecordPreview preview = new(id, accountId, account.View.AccountName, gmsa, planContext.PreviewVersion, planContext.Servers, title, body, reference,
            sent, shortMax, OperationalRecordRules.MaxDescription, chosen, environments,
            new OperationalRecordRequesterView(requester.Status.ToString(), requester.Label, requester.UserId, !string.IsNullOrWhiteSpace(requester.HomeLocationId)),
            [
                new("Kayıt türü", "OR (operasyonel talep)", "Sabit"),
                new("Kısa açıklama", title, shortDescription is null ? "Şablon (plandan)" : "Sizin düzenlemeniz"),
                new("Açıklama", sent, description is null ? "Şablon (plandan) + plan referansı" : "Sizin düzenlemeniz + plan referansı"),
                new("Ortam", chosen ?? "seçilmedi", "Sizin seçiminiz"),
                new("Bildiren ve etkilenen kullanıcı", requester.UserId is { } user ? $"Turuncu Hat kullanıcı kimliği {user}" : "eşleşmedi",
                    "Düğmeye basan kişi (Turuncu Hat birebir eşleme)"),
                new("Konum", string.IsNullOrWhiteSpace(requester.HomeLocationId) ? "bilinmiyor" : requester.HomeLocationId, "Eşleşen kullanıcının Turuncu Hat profili"),
                new("Katalog, aciliyet, öncelik ve form alanları", "sunucu yapılandırmasındaki kodlar", "Sunucu yapılandırması (panelde düzenlenmez)")
            ],
            ports.Creator.Provider, ports.Creator.IsSynthetic, blocked is null, blocked, blocked is null ? null : BlockedLabel(blocked, requester), fingerprint);
        return blocked is null
            ? new OperationalRecordDraftPlan(preview,
                new OperationalRecordReservation(id, accountId, planContext.PreviewId, planContext.PreviewVersion, Guid.Empty, fingerprint!, ports.Creator.Provider, chosen!,
                    requester.UserId!, title, sent),
                new ItsmRequestDraft(requester.UserId!, requester.HomeLocationId, title, sent, chosen!, reference))
            : new OperationalRecordDraftPlan(preview, null, null);
    }

    /// <summary>Plan visible, the account in it, Work and the responsible basis on every account (the repository checks again under lock).</summary>
    private async Task<SaResult<bool>> ReconcilableAsync(SaCaller caller, Guid id, Guid accountId, CancellationToken cancellationToken)
    {
        if (operationalRecords is null)
        {
            return SaResult<bool>.Fail(SaErrors.OperationalRecordCreationDisabled);
        }

        if (await VisiblePlanAsync(caller, id, cancellationToken) is not { } plan || plan.Accounts.All(a => a.View.AccountId != accountId))
        {
            return SaResult<bool>.Fail(SaErrors.NotFound);
        }

        return !caller.Can(ServiceAccountCapabilities.Work) || plan.Accounts.Any(a => !SqlServiceAccountRepository.PlanResponsible(caller.Scope, a.Anchor))
            ? SaResult<bool>.Fail(SaErrors.Forbidden, "scope")
            : true;
    }

    private async Task<SaResult<PlanOperationalRecordsView>> AfterReconcileAsync(SaCaller caller, Guid id, SaResult<Guid> result, CancellationToken cancellationToken)
    {
        SaResult<PlanOperationalRecordsView> view = await VisiblePlanAsync(caller, id, cancellationToken) is { } plan
            ? RecordsView(caller, plan, await repository!.OperationalRecordSubmissionsAsync(id, cancellationToken))
            : SaResult<PlanOperationalRecordsView>.Fail(SaErrors.NotFound);
        return result.IsSuccess ? view : SaResult<PlanOperationalRecordsView>.Fail(result.ErrorCode!, result.Field,
            result.ErrorCode == SaErrors.ChangePlanState ? view.Value : null);
    }

    private PlanOperationalRecordsView RecordsView(SaCaller caller, ChangePlanSnapshot plan, IReadOnlyList<OperationalRecordSubmissionRecord> submissions)
    {
        bool work = caller.Can(ServiceAccountCapabilities.Work) && plan.Accounts.All(a => SqlServiceAccountRepository.PlanResponsible(caller.Scope, a.Anchor));
        bool enabled = operationalRecords is { Creator.IsEnabled: true };
        bool approved = plan.Status is ChangePlanStatus.Approved or ChangePlanStatus.InProgress && plan.Approval is not null;
        string? planBlock = !enabled ? "creationDisabled" : !approved ? ChangePlanRules.IsOpen(plan.Status) ? "planNotApproved" : "planClosed" : null;
        string provider = operationalRecords?.Creator.Provider ?? DisabledItsmRequestCreator.ProviderName;
        return new PlanOperationalRecordsView(plan.Id, plan.Status.ToString(), planBlock is null, provider, operationalRecords?.Creator.IsSynthetic == true, planBlock,
            [.. plan.Accounts.Select(a =>
            {
                OperationalRecordSubmissionRecord[] mine = [.. submissions.Where(s => s.AccountId == a.View.AccountId)];
                OperationalRecordSubmissionRecord? current = mine.FirstOrDefault();
                string? block = planBlock ?? (!work ? "noPermission" : current is not null && OperationalRecordRules.BlocksNewSubmission(current.State) ? "submissionExists" : null);
                return new OperationalRecordAccountView(a.View.AccountId, a.View.AccountName, a.View.TargetGmsaName, current is null ? null : SubmissionView(current),
                    [.. mine.Skip(1).Select(SubmissionView)], block is null, block, block is null ? null : BlockedLabel(block, null),
                    work && operationalRecords is not null && current is not null && OperationalRecordRules.CanLink(current.State),
                    work && operationalRecords is not null && current is not null && OperationalRecordRules.CanConfirmNotCreated(current.State));
            })]);
    }

    private static OperationalRecordSubmissionView SubmissionView(OperationalRecordSubmissionRecord r) =>
        new(r.Id, r.AccountId, r.State.ToString(), OperationalRecordRules.StateLabel(r.State), r.ReasonCode,
            r.ReasonCode is null ? null : OperationalRecordRules.ReasonLabel(r.ReasonCode), r.Provider, IsSynthetic(r.Provider), r.ExternalNumber, r.ExternalId,
            r.LinkVerification, r.Environment, r.ShortDescription, r.Description, r.CreatedBy, r.CreatedAt, r.UpdatedAt,
            OperationalRecordRules.NeedsReconciliation(r.State), r.Events);

    /// <summary>Tracking result to view: every part the ITSM did not prove stays null ("bilinmiyor").</summary>
    private OperationalRecordTrackingView TrackingView(string? number, ItsmRecordTrackingResult result, ItsmRecordLookupStatus? status = null)
    {
        ItsmRecordLookupStatus shown = status ?? result.Status;
        bool activitiesKnown = result.Activities is { State: ItsmTrackingPartState.Known, Value: not null };
        bool commentsKnown = result.Comments is { State: ItsmTrackingPartState.Known, Value: not null };
        ItsmActivity? last = activitiesKnown ? result.Activities!.Value!.LastOrDefault() : null;
        return new OperationalRecordTrackingView(number, shown.ToString(), LookupLabel(shown), Reason(result.Reason), result.State?.LastModifiedText,
            result.State?.RawStatusCode, result.State?.RawStage, activitiesKnown, activitiesKnown ? result.Activities!.Value!.Count : null, last?.RawGroup, last?.RawStatus,
            commentsKnown, commentsKnown ? result.Comments!.Value!.Count : null, commentsKnown ? result.Comments!.Value!.LastCommentText : null, result.Provider,
            IsSynthetic(result.Provider), clock.GetUtcNow());
    }

    private static bool IsSynthetic(string provider) => string.Equals(provider, FakeItsmRequestCreator.ProviderName, StringComparison.Ordinal);

    private static string? Reason(ItsmRecordLookupReason reason) => reason == ItsmRecordLookupReason.None ? null : reason.ToString();

    private static string LookupLabel(ItsmRecordLookupStatus status) => status switch
    {
        ItsmRecordLookupStatus.Found => "Turuncu Hat'ta bulundu",
        ItsmRecordLookupStatus.NotFound => "Turuncu Hat'ta bulunamadı",
        ItsmRecordLookupStatus.Ambiguous => "Birden fazla kayıt eşleşti",
        _ => "Bilinmiyor — Turuncu Hat cevap veremedi"
    };

    private static string BlockedLabel(string reason, OperationalRecordRequester? requester) => reason switch
    {
        "creationDisabled" => "OR açma bu ortamda kapalı (sunucu yapılandırması).",
        "planNotApproved" => "OR yalnız onaylanmış veya uygulamadaki planda açılır.",
        "planClosed" => "Plan kapandı veya iptal edildi; yeni OR açılmaz.",
        "noPermission" => "Bu plan için OR açma yetkiniz veya kapsamınız yok.",
        "submissionExists" => "Bu hesap için açılmış veya sonucu bekleyen bir OR var.",
        "environmentRequired" => "Ortamı seçin.",
        _ => requester?.Label ?? "Turuncu Hat kullanıcı eşlemesi yok; OR açılamaz."
    };
}
