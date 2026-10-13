using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SecureOps.Domain.ServiceAccounts;
using SecureOps.Infrastructure.Access;
using SecureOps.Shared.Contracts.ServiceAccounts;

namespace SecureOps.Infrastructure.ServiceAccounts;

/// <summary>A validated draft: what the person sees (preview) and exactly what a commit writes.</summary>
internal sealed record AccountEventPlan(EventPreview Preview, AccountEventWrite Write);

public sealed partial class ServiceAccountService
{
    /// <summary>S4 (owner default): the person's own short summary stays free text with a bound; mail text is not copied.</summary>
    public const int EventSummaryMax = 1000;

    /// <summary>
    /// Validates a "what happened?" draft and returns what a commit would record and what it would not (EVENT-GUIDANCE-DESIGN.md
    /// §5.3). Writes nothing and audits nothing. Out-of-scope and missing accounts are indistinguishable (404).
    /// </summary>
    public Task<SaResult<EventPreview>> PreviewEventAsync(ClaimsPrincipal principal, AccessOperationContext context, Guid accountId, EventDraft draft,
        CancellationToken cancellationToken) =>
        EventsAsync(principal, context, async caller =>
        {
            SaResult<AccountDetail> detail = await DetailAsync(caller, accountId, cancellationToken);
            if (!detail.IsSuccess)
            {
                return SaResult<EventPreview>.Fail(detail.ErrorCode!, detail.Field);
            }

            SaResult<AccountEventPlan> plan = await PlanEventAsync(detail.Value!, draft, cancellationToken);
            return plan.IsSuccess ? plan.Value!.Preview : SaResult<EventPreview>.Fail(plan.ErrorCode!, plan.Field, plan.Current);
        }, cancellationToken);

    /// <summary>
    /// Records a previewed event in one transaction (§5.4). Everything is revalidated; a different preview digest (409
    /// eventStale), a denied item (403), a refused close (422) or any failure writes nothing. The same idempotency key with the
    /// same draft returns the first result (replayed); with a different draft 409.
    /// </summary>
    public Task<SaResult<EventCommitResult>> CommitEventAsync(ClaimsPrincipal principal, AccessOperationContext context, Guid accountId, EventDraft draft,
        CancellationToken cancellationToken) =>
        EventsAsync(principal, context, async caller =>
        {
            if (draft.IdempotencyKey is not { } key || key == Guid.Empty)
            {
                return SaResult<EventCommitResult>.Fail(SaErrors.IdempotencyKey, "idempotencyKey");
            }

            if (!ValidText(draft.PreviewDigest, 64, true))
            {
                return SaResult<EventCommitResult>.Fail(SaErrors.Invalid, "previewDigest");
            }

            SaResult<AccountDetail> detail = await DetailAsync(caller, accountId, cancellationToken);
            if (!detail.IsSuccess)
            {
                return SaResult<EventCommitResult>.Fail(detail.ErrorCode!, detail.Field);
            }

            // A replay is answered before revalidation: the first commit changed the versions the draft was built on.
            string draftDigest = DraftDigest(accountId, draft);
            if (await repository!.AccountEventByKeyAsync(accountId, key, cancellationToken) is { } stored)
            {
                return stored.DraftDigest == draftDigest
                    ? await EventResultAsync(caller, accountId, stored.Id, stored.Items, true, cancellationToken)
                    : SaResult<EventCommitResult>.Fail(SaErrors.IdempotencyKeyReused, "idempotencyKey");
            }

            SaResult<AccountEventPlan> planned = await PlanEventAsync(detail.Value!, draft, cancellationToken);
            if (!planned.IsSuccess)
            {
                return SaResult<EventCommitResult>.Fail(planned.ErrorCode!, planned.Field, planned.Current);
            }

            AccountEventPlan plan = planned.Value!;
            if (!plan.Preview.CanCommit)
            {
                return SaResult<EventCommitResult>.Fail(SaErrors.Forbidden, "eventItemDenied");
            }

            if (!string.Equals(plan.Preview.PreviewDigest, draft.PreviewDigest, StringComparison.Ordinal))
            {
                return SaResult<EventCommitResult>.Fail(SaErrors.EventStale, "previewDigest");
            }

            AccountEventOutcome outcome = await repository.CommitAccountEventAsync(plan.Write with { IdempotencyKey = key, DraftDigest = draftDigest },
                caller.Actor, cancellationToken);
            return outcome.Status switch
            {
                AccountEventStatus.Committed or AccountEventStatus.Replayed => await EventResultAsync(caller, accountId, outcome.EventId!.Value, outcome.Created,
                    outcome.Status == AccountEventStatus.Replayed, cancellationToken),
                AccountEventStatus.KeyReused => SaResult<EventCommitResult>.Fail(SaErrors.IdempotencyKeyReused, "idempotencyKey"),
                AccountEventStatus.CloseRefused => SaResult<EventCommitResult>.Fail(SaErrors.CloseNotAllowed, "closeRequest",
                    CloseReadinessView(outcome.RequestStatus ?? ServiceAccountRequestStatus.Open, ServiceAccountCloseOutcome.Completed, outcome.Gaps)),
                _ => SaResult<EventCommitResult>.Fail(SaErrors.EventStale, "accountVersion")
            };
        }, cancellationToken);

    /// <summary>Close readiness view of one rule evaluation (shared by the close-readiness route and the event flow).</summary>
    private static CloseReadiness CloseReadinessView(ServiceAccountRequestStatus status, ServiceAccountCloseOutcome outcome, IReadOnlyList<CloseGap> gaps) =>
        new(gaps.Count == 0, status.ToString(), outcome.ToString(),
            [.. gaps.Select(g => new CloseMissing(g.Code, g.Remedy.ToString(), g.SubCodes.Count == 0 ? null : g.SubCodes, g.ExpectedActionType?.ToString(),
                g.RelatedActionId))]);

    private Task<SaResult<T>> EventsAsync<T>(ClaimsPrincipal principal, AccessOperationContext context, Func<SaCaller, Task<SaResult<T>>> operation,
        CancellationToken cancellationToken) =>
        RunAsync(principal, context, ServiceAccountCapabilities.Work, async caller =>
            await repository!.AccountEventsInstalledAsync(cancellationToken) ? await operation(caller) : SaResult<T>.Fail(SaErrors.EventsNotInstalled),
            cancellationToken);

    private async Task<SaResult<EventCommitResult>> EventResultAsync(SaCaller caller, Guid accountId, Guid eventId, IReadOnlyList<EventCreatedRecord> created,
        bool replayed, CancellationToken cancellationToken)
    {
        SaResult<AccountDetail> after = await DetailAsync(caller, accountId, cancellationToken);
        return after.IsSuccess ? new EventCommitResult(after.Value!, eventId, created, replayed) : SaResult<EventCommitResult>.Fail(after.ErrorCode!, after.Field);
    }

    /// <summary>
    /// Validates the draft against the event table (§3) and the caller's permissions (§5.5), derives every record kind on the
    /// server and builds the preview and the write. Reads only.
    /// </summary>
    private async Task<SaResult<AccountEventPlan>> PlanEventAsync(AccountDetail detail, EventDraft draft, CancellationToken cancellationToken)
    {
        static SaResult<AccountEventPlan> Invalid(string field) => SaResult<AccountEventPlan>.Fail(SaErrors.Invalid, field);

        if (!ExactEnum(draft.EventKind, out AccountEventKind kind))
        {
            return Invalid("eventKind");
        }

        if (!ExactEnum(draft.Channel, out AccountEventChannel channel))
        {
            return Invalid("channel");
        }

        if (draft.DatePrecision is not ("DateOnly" or "Unknown") || (draft.DatePrecision == "DateOnly") != draft.OccurredOn.HasValue)
        {
            return Invalid(draft.DatePrecision is "DateOnly" or "Unknown" ? "occurredOn" : "datePrecision");
        }

        if (draft.OccurredOn is { } occurred && occurred > Today)
        {
            return Invalid(ServiceAccountRules.Errors.DateInFuture);
        }

        if (!ValidText(draft.AccountVersion, 64, true))
        {
            return Invalid("accountVersion");
        }

        if (!string.Equals(draft.AccountVersion, detail.Summary.Version, StringComparison.Ordinal))
        {
            return SaResult<AccountEventPlan>.Fail(SaErrors.EventStale, "accountVersion");
        }

        AccountEventShape shape = ServiceAccountEventRules.Shape(kind);
        DateOnly? on = draft.OccurredOn;

        // Blocks the event table does not allow are refused, never ignored (a client cannot smuggle a record kind in).
        if (draft.Action is not null && shape.ActionType is null)
        {
            return Invalid("blockNotAllowed:action");
        }

        if (draft.Ownership is not null && !shape.Ownership)
        {
            return Invalid("blockNotAllowed:ownership");
        }

        if (draft.CreateRequest is not null && shape.CreateRequestType is null)
        {
            return Invalid("blockNotAllowed:createRequest");
        }

        if (draft.RequestId is not null && !shape.LinkRequest)
        {
            return Invalid("blockNotAllowed:requestId");
        }

        if (draft.References is { Count: > 0 } && !shape.References)
        {
            return Invalid("blockNotAllowed:references");
        }

        if (draft.Communication is null && shape.Communication == EventBlockRule.Required)
        {
            return Invalid("communication");
        }

        if (draft.Ownership is null && shape.Ownership)
        {
            return Invalid("ownership");
        }

        if (draft.CloseRequest is not null && (draft.RequestId is null || draft.CloseRequest.Outcome != nameof(ServiceAccountCloseOutcome.Completed)))
        {
            return Invalid("closeRequest");
        }

        RequestView? request = null;
        if (draft.RequestId is { } requestId)
        {
            // Only this account's requests can be linked; another account's request is indistinguishable from a missing one (E3).
            request = detail.Requests.FirstOrDefault(r => r.Id == requestId);
            if (request is null)
            {
                return Invalid("requestAccountMismatch");
            }

            if (request.Status != nameof(ServiceAccountRequestStatus.Open))
            {
                return Invalid("requestNotOpen");
            }

            if (shape.ActionType is { } derived && !ServiceAccountRules.RequestAccepts(Enum.Parse<ServiceAccountActionType>(request.ActionType), derived))
            {
                return Invalid("requestTypeMismatch");
            }
        }

        IReadOnlyList<SaExternalRef>? references = References(draft.References);
        if (references is null)
        {
            return Invalid("references");
        }

        EventCommunicationWrite? communication = null;
        if (draft.Communication is { } mail)
        {
            // A direction/kind the table fixes may be repeated but not changed; otherwise the person chooses an exact value.
            CommunicationDirection direction = shape.Direction ?? default;
            if (shape.Direction is null ? !ExactEnum(mail.Direction, out direction) : mail.Direction is not null && mail.Direction != direction.ToString())
            {
                return Invalid("communication.direction");
            }

            CommunicationKind mailKind = shape.CommunicationKind ?? default;
            if (shape.CommunicationKind is null ? !ExactEnum(mail.Kind, out mailKind) : mail.Kind is not null && mail.Kind != mailKind.ToString())
            {
                return Invalid("communication.kind");
            }
            string? textError = !ValidText(mail.Subject, 400) ? "communication.subject"
                : !ValidText(mail.Summary, EventSummaryMax) ? "communication.summary"
                : !ValidText(mail.ProviderMessageId, 256) ? "communication.providerMessageId"
                : null;
            if (textError is not null)
            {
                return Invalid(textError);
            }

            bool? meaningful = mail.MeaningfulReply ?? (kind == AccountEventKind.OwnerReplied ? true : null);
            communication = new EventCommunicationWrite(direction, mailKind, new CreateCommunicationRequest(direction.ToString(), mailKind.ToString(), [detail.Summary.Id],
                on, null, mail.ContactTeamId, mail.ContactPersonId, mail.Subject, mail.Summary, null, mail.ProviderMessageId, meaningful));
        }

        EventActionWrite? action = null;
        if (shape.ActionType is { } actionType)
        {
            EventAction performer = draft.Action ?? new EventAction();
            if (!ValidText(performer.EvidenceNote, 2000))
            {
                return Invalid("action.evidenceNote");
            }

            action = new EventActionWrite(actionType, shape.RecordKind!.Value, new ReportActionRequest(actionType.ToString(), ServiceAccountEventRules.ActionResult.ToString(),
                shape.RecordKind.Value.ToString(), draft.RequestId, on, null, performer.PerformerTeamId, performer.PerformerPersonId, performer.EvidenceNote));
        }

        EventRequestWrite? create = null;
        if (draft.CreateRequest is { } opening)
        {
            ServiceAccountActionType type = shape.CreateRequestType!.Value;
            if (draft.RequestId is not null || opening.ActionType is not null && opening.ActionType != type.ToString())
            {
                return Invalid("createRequest");
            }

            if (!ValidText(opening.Notes, 4000))
            {
                return Invalid("createRequest.notes");
            }

            create = new EventRequestWrite(type, opening.TargetTeamId, opening.Notes);
        }

        EventOwnershipWrite? ownership = null;
        if (draft.Ownership is { } proposal)
        {
            if (proposal.TeamId is null && proposal.PersonId is null || !ValidText(proposal.Reason, 1000, true))
            {
                return Invalid(proposal.TeamId is null && proposal.PersonId is null ? "ownership.teamId" : "ownership.reason");
            }

            ownership = new EventOwnershipWrite(proposal.TeamId, proposal.PersonId, proposal.EffectiveFrom, proposal.Reason.Trim());
        }

        // Contacts, performers and owners are directory identities only; a free name is never accepted (§5.5).
        Guid[] teams = [.. new[] { draft.Communication?.ContactTeamId, draft.Action?.PerformerTeamId, create?.TargetTeamId, ownership?.TeamId }.OfType<Guid>()];
        Guid[] people = [.. new[] { draft.Communication?.ContactPersonId, draft.Action?.PerformerPersonId, ownership?.PersonId }.OfType<Guid>()];
        if (!await repository!.DirectoryIdsKnownAsync(teams, people, cancellationToken))
        {
            return Invalid("directory");
        }

        // Permissions (§5.5): the responsible basis may write everything; a participant only records on its own open request and
        // never opens a request, proposes ownership or closes a request; anyone else writes nothing.
        AccountPermissions permissions = detail.Permissions;
        bool participantRequest = draft.RequestId is { } own && permissions.CanWorkRequest(own);
        string? Denied(bool accountWide) =>
            permissions.Work ? null
            : !permissions.CanWorkAnyRequest ? "OutOfScope"
            : accountWide || !participantRequest ? "NotParticipantRequest"
            : null;

        List<EventPreviewItem> items = [];
        List<EventWarning> warnings = [];
        Guid? existingMail = null;
        if (communication is not null)
        {
            existingMail = ServiceAccountText.Clean(communication.Request.ProviderMessageId) is { } provider
                ? await repository.CommunicationByProviderIdAsync(provider, cancellationToken) : null;
            if (existingMail is not null)
            {
                warnings.Add(new EventWarning("MailDuplicate", "communication.providerMessageId"));
            }

            CreateCommunicationRequest m = communication.Request;
            items.Add(Item("Communication", existingMail is null ? "Create" : "Link", existingMail,
                existingMail is null ? "Yazışma kaydı" : "Yazışma (aynı mail zaten kayıtlı; bu hesaba bağlanır)", Denied(false),
                ("direction", m.Direction), ("kind", m.Kind), ("occurredOn", Date(on)), ("contactTeamId", Id(m.ContactTeamId)), ("contactPersonId", Id(m.ContactPersonId)),
                ("subject", m.Subject), ("summary", m.Summary), ("meaningfulReply", m.MeaningfulReply?.ToString()),
                ("providerMessageId", m.ProviderMessageId is null ? null : "var")));
        }

        if (action is not null)
        {
            ReportActionRequest a = action.Request;
            items.Add(Item("Action", "Create", null, "İşlem bildirimi — Bildirildi, doğrulanmadı", Denied(false),
                ("actionType", a.ActionType), ("result", a.Result), ("recordKind", a.RecordKind), ("actualOn", Date(a.ActualOn)), ("requestId", Id(a.RequestId)),
                ("performerTeamId", Id(a.PerformerTeamId)), ("performerPersonId", Id(a.PerformerPersonId)), ("evidenceNote", a.EvidenceNote)));
        }

        foreach (SaExternalRef reference in references)
        {
            items.Add(Item("Reference", "Link", null, "Referans: " + reference.Type + " " + ServiceAccountText.Clean(reference.Number), Denied(false),
                ("type", reference.Type), ("number", ServiceAccountText.Clean(reference.Number)), ("url", reference.Url)));
        }

        bool moveLastReply = false;
        if (shape.MovesLastReply && request is not null)
        {
            if (on is not { } replied)
            {
                warnings.Add(new EventWarning("ReplyDateUnknown", "occurredOn"));
            }
            else if (request.LastReplyOn is { } last && last >= replied)
            {
                warnings.Add(new EventWarning("ReplyDateEarlier", "occurredOn"));
            }
            else
            {
                moveLastReply = true;
                items.Add(Item("RequestUpdate", "Update", request.Id, "Talep: son yanıt tarihi ileri alınır", Denied(false),
                    ("lastReplyOn", Date(replied)), ("previousLastReplyOn", Date(request.LastReplyOn))));
            }
        }

        if (create is not null)
        {
            items.Add(Item("RequestCreate", "Create", null, "Yeni talep açılır", Denied(true),
                ("actionType", create.Type.ToString()), ("targetTeamId", Id(create.TargetTeamId)), ("notes", create.Notes)));
        }

        if (ownership is not null)
        {
            items.Add(Item("OwnershipProposal", "Create", null, "Sahiplik önerisi — karar değil", Denied(true),
                ("mode", "Propose"), ("teamId", Id(ownership.TeamId)), ("personId", Id(ownership.PersonId)), ("effectiveFrom", Date(ownership.EffectiveFrom)),
                ("reason", ownership.Reason)));
        }

        if (draft.CloseRequest is not null && request is not null)
        {
            items.Add(Item("RequestClose", "Update", request.Id, "Talep kapatılır (Tamamlandı)", Denied(true),
                ("outcome", nameof(ServiceAccountCloseOutcome.Completed))));
        }

        if (on is null)
        {
            warnings.Add(new EventWarning("DateUnknown", "occurredOn"));
        }

        if (kind == AccountEventKind.AccountDeleted && !references.Any(r => r.Type == nameof(ExternalRecordType.OR)))
        {
            // A deletion closure is verified only with an OR (rule 6); without it the report can be recorded but not verified later.
            warnings.Add(new EventWarning("OrMissingForDeletion", "references"));
        }

        CloseReadiness? readiness = null;
        if (request is not null && await repository.RequestCloseFactsAsync(request.Id, cancellationToken) is { } facts)
        {
            // What the close rule would say after these records (the commit re-runs the same rule on the written rows).
            ActionFacts[] actions = action is null ? [.. facts.Actions]
                : [.. facts.Actions, new ActionFacts(action.Type, ServiceAccountEventRules.ActionResult, action.RecordKind, on, null, false,
                    !string.IsNullOrWhiteSpace(action.Request.EvidenceNote), references.Any(r => r.Type == nameof(ExternalRecordType.OR)), false)];
            readiness = CloseReadinessView(facts.Status, ServiceAccountCloseOutcome.Completed,
                ServiceAccountRules.EvaluateClose(facts.Status, facts.Type, ServiceAccountCloseOutcome.Completed, null, actions, facts.OwnershipConfirmed));
        }

        List<string> notRecorded = ["Verification", "AccountClosure"];
        if (draft.CloseRequest is null)
        {
            notRecorded.Add("RequestClose");
        }

        if (kind == AccountEventKind.OwnerChanged)
        {
            notRecorded.Add("OwnershipDecision");
        }

        if (kind == AccountEventKind.GmsaConverted)
        {
            notRecorded.Add("PlanChecklist");
        }

        List<EventFollowUp> followUps = [];
        if (action is not null)
        {
            followUps.Add(new EventFollowUp("Verify"));
        }

        if (request is not null && draft.CloseRequest is null)
        {
            followUps.Add(new EventFollowUp("CloseRequest"));
        }

        followUps.AddRange(kind switch
        {
            AccountEventKind.GmsaConverted => [new EventFollowUp("OpenPlan")],
            AccountEventKind.ServiceMoved => [new EventFollowUp("UsageTab")],
            AccountEventKind.OwnerChanged => [new EventFollowUp("OwnershipDecision")],
            _ => (EventFollowUp[])[]
        });

        string digest = PreviewDigest(detail.Summary.Id, draft, detail.Summary.Version, request?.Version);
        EventPreview preview = new(digest, items, notRecorded, warnings, readiness, followUps, items.All(i => i.Allowed));
        AccountEventWrite write = new(detail.Summary.Id, kind, on, channel, Guid.Empty, string.Empty, detail.Summary.Version, request?.Id, request?.Version,
            communication, action, references, moveLastReply, create, ownership, draft.CloseRequest is not null);
        return new AccountEventPlan(preview, write);
    }

    private static EventPreviewItem Item(string kind, string operation, Guid? target, string label, string? denied, params (string Name, string? Value)[] fields) =>
        new(kind, operation, target, label, [.. fields.Select(f => new EventField(f.Name, f.Value))], denied is null, denied);

    private static string? Date(DateOnly? value) => value?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    private static string? Id(Guid? value) => value?.ToString("D");

    /// <summary>Exact member name only: no numbers, letter-case variants or comma-joined combinations.</summary>
    private static bool ExactEnum<TEnum>(string? value, out TEnum parsed) where TEnum : struct, Enum =>
        Enum.TryParse(value, false, out parsed) && parsed.ToString() == value;

    /// <summary>Idempotency identity of a draft: the canonical body without the key and the preview digest.</summary>
    private static string DraftDigest(Guid accountId, EventDraft draft) =>
        Sha256(JsonSerializer.Serialize(new { ServiceAccountEventRules.RuleSet, AccountId = accountId, Draft = draft with { IdempotencyKey = null, PreviewDigest = null } }));

    /// <summary>What the person confirmed: the canonical draft plus the current versions of the account and the linked request (E6).</summary>
    private static string PreviewDigest(Guid accountId, EventDraft draft, string accountVersion, string? requestVersion) =>
        Sha256(JsonSerializer.Serialize(new
        {
            ServiceAccountEventRules.RuleSet,
            AccountId = accountId,
            Draft = draft with { IdempotencyKey = null, PreviewDigest = null },
            AccountVersion = accountVersion,
            RequestVersion = requestVersion
        }));

    private static string Sha256(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
