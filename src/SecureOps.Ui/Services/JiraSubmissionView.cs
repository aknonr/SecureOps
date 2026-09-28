using SecureOps.Shared.Contracts.OperationalRecords;

namespace SecureOps.Ui.Services;

/// <summary>
/// Resolves where a Jira-only submission stands, from authoritative record state first and the
/// page's last command second.
/// </summary>
/// <remarks>
/// <para>
/// The ordering is the safety property. A persisted issue key outranks everything except an
/// unresolved outcome, and a command response never outranks the re-read record: a
/// <see cref="JiraTransferResponse"/> is the server acknowledging a request, not a saved Jira
/// issue. Only <see cref="OperationalRecordResponse.JiraIssueKey"/> / <c>JiraExists</c> on the
/// re-read record is shown as a created issue.
/// </para>
/// <para>
/// Nothing here grants an action. Whether create is permitted still comes from
/// <see cref="OperationalRecordView.ActionsFor"/> and capability; this only names the state so the
/// operator can tell "ready", "sent", "created", "refused" and "unknown" apart.
/// </para>
/// </remarks>
public static class JiraSubmissionView
{
    /// <summary>What the submission panel presents.</summary>
    public enum Phase
    {
        /// <summary>No submittable preview; the reason comes from workflow actions or capability.</summary>
        Unavailable,

        /// <summary>A review-only draft is shown; it can never be submitted.</summary>
        ReviewOnly,

        /// <summary>A server preview exists and the workflow permits create for this operator.</summary>
        Ready,

        /// <summary>The create or retry command is in flight.</summary>
        Submitting,

        /// <summary>The re-read record carries a persisted Jira issue key.</summary>
        Created,

        /// <summary>The server answered the command, but the re-read record has no saved key.</summary>
        Acknowledged,

        /// <summary>The server refused the command; no key is saved and the outcome is not marked unknown.</summary>
        Rejected,

        /// <summary>Whether Jira created an issue is unknown; reconciliation is required.</summary>
        Uncertain
    }

    /// <summary>The outcome of the page's last create or retry command.</summary>
    /// <param name="Transfer">The server's command response, when one was read.</param>
    /// <param name="Problem">The translated failure, when the command failed.</param>
    public sealed record Attempt(JiraTransferResponse? Transfer, UiProblem? Problem);

    /// <summary>Stable codes whose presence means the chosen request type has no approved Jira mapping.</summary>
    /// <remarks>Rendered as guidance only; the server has already applied them as blockers.</remarks>
    public static readonly IReadOnlySet<string> UnsupportedTypeCodes = new HashSet<string>(StringComparer.Ordinal)
    {
        "TypeMappingPending",
        "RetirementMappingPending",
        "ApplicationMappingPending",
        "CategoryUnsupported",
        "CategoryUnknown"
    };

    /// <summary>Resolves the submission phase.</summary>
    /// <param name="record">Authoritative record, as last read.</param>
    /// <param name="preview">The server preview currently held by the page, if any.</param>
    /// <param name="actions">Workflow actions from <see cref="OperationalRecordView.ActionsFor"/>.</param>
    /// <param name="canCreate">Whether the operator holds the create capability.</param>
    /// <param name="submitting">Whether a create or retry command is in flight.</param>
    /// <param name="attempt">The last command outcome, cleared by an explicit refresh.</param>
    /// <returns>The phase to present.</returns>
    public static Phase PhaseOf(
        OperationalRecordResponse record,
        JiraPreviewResponse? preview,
        OperationalRecordView.Actions actions,
        bool canCreate,
        bool submitting,
        Attempt? attempt)
    {
        if (submitting)
        {
            return Phase.Submitting;
        }

        bool hasJira = OperationalRecordView.HasJira(record);

        // An unacknowledged command stays uncertain until the operator explicitly reloads: the
        // automatic re-read can race a request the server has not yet started processing.
        if (!hasJira && (OperationalRecordView.OutcomeUnknown(record) || IsUncertain(attempt?.Problem)))
        {
            return Phase.Uncertain;
        }

        if (hasJira)
        {
            return Phase.Created;
        }

        if (attempt?.Transfer is not null)
        {
            return Phase.Acknowledged;
        }

        if (attempt?.Problem is not null)
        {
            return Phase.Rejected;
        }

        if (preview is { ReviewOnly: true })
        {
            return Phase.ReviewOnly;
        }

        return preview is not null && actions.Create && canCreate ? Phase.Ready : Phase.Unavailable;
    }

    /// <summary>Whether a command failure is the unresolved-outcome presentation.</summary>
    /// <param name="problem">Translated failure.</param>
    /// <returns><c>true</c> for the reconciliation stage.</returns>
    public static bool IsUncertain(UiProblem? problem) =>
        string.Equals(problem?.Stage, UiProblemFactory.ReconciliationStage, StringComparison.Ordinal);

    /// <summary>What happens to the source OR, as fixed by the server preview.</summary>
    /// <param name="sourceCloseRequested">The preview's close intent.</param>
    /// <returns>Operator-facing statement.</returns>
    public static string SourceOutcome(bool sourceCloseRequested) => sourceCloseRequested
        ? "Jira kaydı oluşturulduktan sonra Turuncu Hat kaydının kapatılması denenecek."
        : "Turuncu Hat kaydı açık kalacak. WASAS bu gönderimde kaynak kaydı kapatmaz ve kaynak kayda durum veya yorum yazmaz.";

    /// <summary>Why transfer-and-close is not offered on this screen.</summary>
    /// <param name="record">Authoritative record.</param>
    /// <returns>Operator-facing explanation.</returns>
    public static string TransferAndCloseReason(OperationalRecordResponse record) => record.SourceCloseEnabled
        ? "Bu ekranda aktar-ve-kapat işlemi sunulmaz. Kapatma niyeti sunucu önizlemesinde sabitlenir; kaynak kapatma ayrı onay ve etkinleştirme gerektirir."
        : "Bu ekranda aktar-ve-kapat işlemi sunulmaz. Kaynak kapatma bu dağıtımda kapalıdır; ayrı onay ve etkinleştirme gerektirir.";

    /// <summary>Guidance for server blockers that mean the request type cannot be published.</summary>
    /// <param name="record">Authoritative record.</param>
    /// <param name="preview">The held preview, if any.</param>
    /// <returns>Distinct Turkish guidance lines, in server order.</returns>
    public static IReadOnlyList<string> UnsupportedTypeGuidance(OperationalRecordResponse record, JiraPreviewResponse? preview) =>
        record.BlockingConditions
            .Concat(preview?.BlockingConditions ?? [])
            .Where(UnsupportedTypeCodes.Contains)
            .Select(SdmEvidenceView.Guidance)
            .Distinct(StringComparer.Ordinal)
            .ToList();
}
