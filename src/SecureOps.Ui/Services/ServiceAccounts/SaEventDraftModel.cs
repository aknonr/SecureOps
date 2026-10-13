using SecureOps.Domain.ServiceAccounts;
using SecureOps.Shared.Contracts.ServiceAccounts;

namespace SecureOps.Ui.Services.ServiceAccounts;

/// <summary>
/// What the person typed in the "what happened?" flow. <see cref="Build"/> sends only the blocks the event table allows for the
/// chosen event (a field typed for another event is not smuggled in) and never a result, record kind, action type or verification
/// field: the server derives those. Pasted mail text is not part of this model; it is never kept.
/// </summary>
public sealed class SaEventDraftModel
{
    /// <summary>One reference row.</summary>
    public sealed class ReferenceRow
    {
        /// <summary>OCO, OR, JIRA or OTHER.</summary>
        public string Type { get; set; } = "OCO";

        /// <summary>Record number.</summary>
        public string? Number { get; set; }
    }

    /// <summary>Most references one event links (the server's bound).</summary>
    public const int MaxReferences = 10;

    /// <summary>Chosen event kind.</summary>
    public string? Kind { get; set; }

    /// <summary>The real date of the event.</summary>
    public DateTime? OccurredOn { get; set; }

    /// <summary>The date is not known; no date is invented.</summary>
    public bool DateUnknown { get; set; }

    /// <summary>The date came from a mail hint whose year was assumed; the person must check it.</summary>
    public bool DateAssumed { get; set; }

    /// <summary>How the person learned it.</summary>
    public string Channel { get; set; } = "Mail";

    /// <summary>Linked open request.</summary>
    public Guid? RequestId { get; set; }

    /// <summary>Also close the linked request (Completed); off by default.</summary>
    public bool CloseRequest { get; set; }

    /// <summary>Ask to open a new request (moved service, other).</summary>
    public bool CreateRequest { get; set; }

    /// <summary>New request's target team.</summary>
    public Guid? CreateTargetTeamId { get; set; }

    /// <summary>New request's notes.</summary>
    public string? CreateNotes { get; set; }

    /// <summary>Record the correspondence when it is optional for the event.</summary>
    public bool IncludeCommunication { get; set; }

    /// <summary>Mail direction when the event does not fix it.</summary>
    public string Direction { get; set; } = nameof(CommunicationDirection.Incoming);

    /// <summary>Mail kind when the event does not fix it.</summary>
    public string CommunicationKind { get; set; } = nameof(Domain.ServiceAccounts.CommunicationKind.Information);

    /// <summary>Contact team (directory).</summary>
    public Guid? ContactTeamId { get; set; }

    /// <summary>Contact person (directory).</summary>
    public Guid? ContactPersonId { get; set; }

    /// <summary>Mail subject.</summary>
    public string? Subject { get; set; }

    /// <summary>The person's own short summary (not mail text).</summary>
    public string? Summary { get; set; }

    /// <summary>Team that performed the reported action.</summary>
    public Guid? PerformerTeamId { get; set; }

    /// <summary>Person that performed the reported action.</summary>
    public Guid? PerformerPersonId { get; set; }

    /// <summary>Short evidence note.</summary>
    public string? EvidenceNote { get; set; }

    /// <summary>OR / OCO / JIRA numbers.</summary>
    public List<ReferenceRow> References { get; } = [];

    /// <summary>Proposed owner team.</summary>
    public Guid? OwnerTeamId { get; set; }

    /// <summary>Proposed owner person.</summary>
    public Guid? OwnerPersonId { get; set; }

    /// <summary>Proposed ownership start.</summary>
    public DateTime? OwnerEffectiveFrom { get; set; }

    /// <summary>Why the ownership changed.</summary>
    public string? OwnerReason { get; set; }

    /// <summary>
    /// What is still missing before a preview can be asked for (input completeness only; every rule is checked by the server), or
    /// null when complete.
    /// </summary>
    public string? Incomplete(AccountPermissions permissions)
    {
        if (ServiceAccountEventText.Shape(Kind) is not { } shape)
        {
            return "Önce ne olduğunu seçin.";
        }

        if (!DateUnknown && OccurredOn is null)
        {
            return "Gelişmenin gerçek tarihini girin veya \"Tarih bilinmiyor\"u işaretleyin.";
        }

        if (!permissions.Work && RequestId is null)
        {
            return "Ekibinize atanmış bir açık talep seçin; yalnız o talep üzerinden bildirebilirsiniz.";
        }

        if (shape.Ownership && OwnerTeamId is null && OwnerPersonId is null)
        {
            return "Yeni sahip ekibi veya kişiyi seçin.";
        }

        return shape.Ownership && string.IsNullOrWhiteSpace(OwnerReason) ? "Sahiplik önerisi için gerekçe yazın." : null;
    }

    /// <summary>The draft for preview (and, with the key and digest added, for commit).</summary>
    public EventDraft Build(AccountDetail detail)
    {
        AccountEventShape shape = ServiceAccountEventText.Shape(Kind) ?? throw new InvalidOperationException("No event kind chosen.");
        bool work = detail.Permissions.Work;
        Guid? request = shape.LinkRequest ? RequestId : null;

        EventCommunication? communication = shape.Communication == EventBlockRule.Required || shape.Communication == EventBlockRule.Optional && IncludeCommunication
            ? new EventCommunication(shape.Direction is null ? Direction : null, shape.CommunicationKind is null ? CommunicationKind : null, ContactTeamId, ContactPersonId,
                Clean(Subject), Clean(Summary))
            : null;

        EventAction? action = shape.ActionType is not null && (PerformerTeamId is not null || PerformerPersonId is not null || Clean(EvidenceNote) is not null)
            ? new EventAction(PerformerTeamId, PerformerPersonId, Clean(EvidenceNote))
            : null;

        SaExternalRef[] references = shape.References
            ? [.. References.Where(r => !string.IsNullOrWhiteSpace(r.Number)).Select(r => new SaExternalRef(r.Type, r.Number!.Trim()))]
            : [];

        return new EventDraft(
            Kind!,
            DateUnknown ? null : ServiceAccountUiText.ToDate(OccurredOn),
            DateUnknown ? "Unknown" : "DateOnly",
            Channel,
            detail.Summary.Version,
            request,
            shape.CreateRequestType is not null && CreateRequest && work && request is null ? new EventCreateRequest(null, CreateTargetTeamId, Clean(CreateNotes)) : null,
            request is not null && CloseRequest && work ? new EventCloseRequest("Completed") : null,
            communication,
            action,
            references.Length > 0 ? references : null,
            shape.Ownership ? new EventOwnership(OwnerTeamId, OwnerPersonId, OwnerReason?.Trim() ?? string.Empty, ServiceAccountUiText.ToDate(OwnerEffectiveFrom)) : null);
    }

    /// <summary>Adds a suggested reference unless the same number is already there.</summary>
    public void AddReference(string type, string number)
    {
        if (References.Count < MaxReferences && !References.Any(r => string.Equals(r.Number?.Trim(), number, StringComparison.OrdinalIgnoreCase)))
        {
            References.Add(new ReferenceRow { Type = type, Number = number });
        }
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
