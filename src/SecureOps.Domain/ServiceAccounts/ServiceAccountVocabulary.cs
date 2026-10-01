namespace SecureOps.Domain.ServiceAccounts;

/// <summary>Controlled expected/performed action vocabulary (rule 2); free text is stored separately.</summary>
public enum ServiceAccountActionType
{
    /// <summary>Action not yet determined ("Değerlendirilecek").</summary>
    Evaluate,
    /// <summary>Review / investigation.</summary>
    Review,
    /// <summary>Ownership confirmation request.</summary>
    OwnershipConfirmation,
    /// <summary>Password change; usually an intermediate step.</summary>
    PasswordChange,
    /// <summary>Deletion; a verified deletion closure also needs an OR reference.</summary>
    Deletion,
    /// <summary>Handover whose target is a later gMSA transition; not proof of suitability.</summary>
    GmsaHandover,
    /// <summary>Actual gMSA conversion event.</summary>
    GmsaConversion
}

/// <summary>Plan, reported action and verification are separate stages of one action identity (rules 3-4).</summary>
public enum ServiceAccountActionResult
{
    /// <summary>Planned only; never counted as performed.</summary>
    Planned,
    /// <summary>Reported as performed.</summary>
    Performed,
    /// <summary>Performed and verified; still one action.</summary>
    Verified
}

/// <summary>Whether an action keeps the account open or is a closure candidate (rule 5).</summary>
public enum ServiceAccountRecordKind
{
    /// <summary>Keeps tracking open.</summary>
    Intermediate,
    /// <summary>Candidate for a verified account closure; not a closure by itself.</summary>
    Closure
}

/// <summary>Request lifecycle; closing is explicit and validated (rule 9).</summary>
public enum ServiceAccountRequestStatus
{
    /// <summary>Open.</summary>
    Open,
    /// <summary>Closed with an outcome.</summary>
    Closed
}

/// <summary>Explicit request close outcome.</summary>
public enum ServiceAccountCloseOutcome
{
    /// <summary>Expected work completed; server checks linked actions.</summary>
    Completed,
    /// <summary>No longer needed; reason required.</summary>
    NotNeeded,
    /// <summary>Cancelled; reason required.</summary>
    Cancelled
}

/// <summary>Communication direction.</summary>
public enum CommunicationDirection
{
    /// <summary>Received.</summary>
    Incoming,
    /// <summary>Sent.</summary>
    Outgoing
}

/// <summary>Communication kind; drafts are never counted as sent mail.</summary>
public enum CommunicationKind
{
    /// <summary>First request.</summary>
    FirstRequest,
    /// <summary>Reply.</summary>
    Reply,
    /// <summary>Information.</summary>
    Information,
    /// <summary>Reminder.</summary>
    Reminder,
    /// <summary>Draft; not a sent mail.</summary>
    Draft
}

/// <summary>Precision of a business time; unknown is never replaced by a fabricated date.</summary>
public enum TimePrecision
{
    /// <summary>Date unknown.</summary>
    Unknown,
    /// <summary>Date only, no time of day.</summary>
    DateOnly,
    /// <summary>Verified instant.</summary>
    Instant
}

/// <summary>Handover proposal is not acceptance (rule 10).</summary>
public enum HandoverStatus
{
    /// <summary>Reported to the handover cohort only.</summary>
    Proposed,
    /// <summary>Accepted by the authorized target team with evidence.</summary>
    Accepted,
    /// <summary>Rejected.</summary>
    Rejected
}

/// <summary>gMSA suitability; never inferred from a database mention.</summary>
public enum GmsaSuitability
{
    /// <summary>Unknown.</summary>
    Unknown,
    /// <summary>Under review.</summary>
    Review,
    /// <summary>Eligible with decision evidence.</summary>
    Eligible,
    /// <summary>Not eligible with decision evidence.</summary>
    Ineligible
}

/// <summary>Finding status; a finding is never an action (rule 15).</summary>
public enum FindingStatus
{
    /// <summary>Open.</summary>
    Open,
    /// <summary>In review.</summary>
    InReview,
    /// <summary>Closed.</summary>
    Closed
}

/// <summary>Real scan outcome.</summary>
public enum FindingScanResult
{
    /// <summary>Scan succeeded.</summary>
    Success,
    /// <summary>Target unreachable; proves nothing about usage.</summary>
    Unreachable,
    /// <summary>Scan failed.</summary>
    Failed,
    /// <summary>Partial coverage.</summary>
    Partial,
    /// <summary>Unknown.</summary>
    Unknown
}

/// <summary>Match outcome; no match does not mean unused.</summary>
public enum FindingMatchResult
{
    /// <summary>Match found.</summary>
    Match,
    /// <summary>No match in the covered window.</summary>
    NoMatch,
    /// <summary>Uncertain.</summary>
    Uncertain
}

/// <summary>Distinct external record types (rule 7).</summary>
public enum ExternalRecordType
{
    /// <summary>Service request / operation record.</summary>
    OR,
    /// <summary>Change record.</summary>
    OCO,
    /// <summary>Jira issue key.</summary>
    JIRA,
    /// <summary>Other system.</summary>
    OTHER
}

/// <summary>Ownership assignment lifecycle.</summary>
public enum OwnershipState
{
    /// <summary>Proposed; not an owner yet.</summary>
    Proposed,
    /// <summary>Confirmed by an authorized decision.</summary>
    Confirmed,
    /// <summary>Ended.</summary>
    Ended,
    /// <summary>Rejected proposal.</summary>
    Rejected
}

/// <summary>Scope grant kind.</summary>
public enum ScopeKind
{
    /// <summary>Whole module.</summary>
    All,
    /// <summary>Organization subtree.</summary>
    Organization,
    /// <summary>One team.</summary>
    Team
}

/// <summary>Turkish operator labels and legacy spreadsheet label parsing for the controlled vocabulary.</summary>
public static class ServiceAccountLabels
{
    private static readonly Dictionary<ServiceAccountActionType, string> _actions = new()
    {
        [ServiceAccountActionType.Evaluate] = "Değerlendirilecek",
        [ServiceAccountActionType.Review] = "İnceleme",
        [ServiceAccountActionType.OwnershipConfirmation] = "Sahiplik teyidi",
        [ServiceAccountActionType.PasswordChange] = "Parola değişimi",
        [ServiceAccountActionType.Deletion] = "Silme",
        [ServiceAccountActionType.GmsaHandover] = "gMSA ile devir",
        [ServiceAccountActionType.GmsaConversion] = "gMSA geçişi"
    };

    private static readonly Dictionary<ServiceAccountActionResult, string> _results = new()
    {
        [ServiceAccountActionResult.Planned] = "Planlandı",
        [ServiceAccountActionResult.Performed] = "Gerçekleşti",
        [ServiceAccountActionResult.Verified] = "Doğrulandı"
    };

    /// <summary>Turkish label of an action type.</summary>
    public static string Action(ServiceAccountActionType value) => _actions[value];

    /// <summary>Turkish label of an action result.</summary>
    public static string Result(ServiceAccountActionResult value) => _results[value];

    /// <summary>Turkish label of a record kind.</summary>
    public static string Kind(ServiceAccountRecordKind value) => value == ServiceAccountRecordKind.Closure ? "Hesap kapanışı" : "Ara adım";

    /// <summary>Turkish label of a communication direction.</summary>
    public static string Direction(CommunicationDirection value) => value == CommunicationDirection.Incoming ? "Gelen" : "Giden";

    /// <summary>Turkish label of a communication kind.</summary>
    public static string CommunicationKindLabel(CommunicationKind value) => value switch
    {
        CommunicationKind.FirstRequest => "İlk talep",
        CommunicationKind.Reply => "Yanıt",
        CommunicationKind.Information => "Bilgilendirme",
        CommunicationKind.Reminder => "Hatırlatma",
        _ => "Taslak"
    };

    /// <summary>Turkish label of a handover status.</summary>
    public static string Handover(HandoverStatus value) => value switch
    {
        HandoverStatus.Proposed => "Devir kapsamına bildirildi",
        HandoverStatus.Accepted => "Kabul edildi",
        _ => "Reddedildi"
    };

    /// <summary>Turkish label of gMSA suitability.</summary>
    public static string Suitability(GmsaSuitability value) => value switch
    {
        GmsaSuitability.Review => "İnceleniyor",
        GmsaSuitability.Eligible => "Uygun",
        GmsaSuitability.Ineligible => "Uygun değil",
        _ => "Bilinmiyor"
    };

    /// <summary>Turkish label of a finding status.</summary>
    public static string Finding(FindingStatus value) => value switch
    {
        FindingStatus.Open => "Açık",
        FindingStatus.InReview => "İnceleniyor",
        _ => "Kapalı"
    };

    /// <summary>Parses a legacy action label exactly (after normalization); unknown labels are not guessed.</summary>
    public static ServiceAccountActionType? ParseAction(string? label) => Parse(label, _actions);

    /// <summary>Parses a legacy result label.</summary>
    public static ServiceAccountActionResult? ParseResult(string? label) => Parse(label, _results);

    /// <summary>Parses a legacy record-kind label.</summary>
    public static ServiceAccountRecordKind? ParseKind(string? label) =>
        ServiceAccountText.Same(label, "Hesap kapanışı") ? ServiceAccountRecordKind.Closure
        : ServiceAccountText.Same(label, "Ara adım") ? ServiceAccountRecordKind.Intermediate : null;

    /// <summary>Parses a legacy communication direction.</summary>
    public static CommunicationDirection? ParseDirection(string? label) =>
        ServiceAccountText.Same(label, "Gelen") ? CommunicationDirection.Incoming
        : ServiceAccountText.Same(label, "Giden") ? CommunicationDirection.Outgoing : null;

    /// <summary>Parses a legacy communication kind.</summary>
    public static CommunicationKind? ParseCommunicationKind(string? label) =>
        Enum.GetValues<CommunicationKind>().Cast<CommunicationKind?>()
            .FirstOrDefault(kind => ServiceAccountText.Same(label, CommunicationKindLabel(kind!.Value)));

    /// <summary>Parses a legacy request status.</summary>
    public static ServiceAccountRequestStatus? ParseRequestStatus(string? label) =>
        ServiceAccountText.Same(label, "Açık") ? ServiceAccountRequestStatus.Open
        : ServiceAccountText.Same(label, "Kapalı") ? ServiceAccountRequestStatus.Closed : null;

    private static T? Parse<T>(string? label, Dictionary<T, string> labels) where T : struct =>
        labels.Where(pair => ServiceAccountText.Same(label, pair.Value)).Select(pair => (T?)pair.Key).FirstOrDefault();
}
