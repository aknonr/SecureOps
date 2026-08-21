using MudBlazor;
using SecureOps.Domain.OperationalRecords;
using SecureOps.Shared.Contracts.OperationalRecords;
using SecureOps.Ui.Shared.Components;

namespace SecureOps.Ui.Services;

/// <summary>
/// Turns an operational record's authoritative state into what the operator sees.
/// </summary>
/// <remarks>
/// <para>
/// Three independent things are deliberately kept apart, because collapsing them into one "status"
/// is how an operator ends up creating a duplicate Jira issue:
/// </para>
/// <list type="bullet">
///   <item><description><b>Workflow stage</b> — how far the transfer has progressed.</description></item>
///   <item><description><b>Ownership</b> — whether anyone currently holds the claim.</description></item>
///   <item><description><b>Source freshness</b> — whether the source was revalidated recently.</description></item>
/// </list>
/// <para>
/// Two states carry the most operational weight and are never toned like ordinary failures:
/// <see cref="OperationalRecordWorkflowState.OperationalRecordCloseFailed"/> means the Jira issue
/// <i>exists</i> and only the source close is outstanding, and
/// <see cref="OperationalRecordWorkflowState.CreatingJira"/> means an attempt was in flight and its
/// outcome is not known. In both cases creating another issue risks a duplicate, so neither offers a
/// create action.
/// </para>
/// </remarks>
public static class OperationalRecordView
{
    /// <summary>
    /// What the operator may do with a record, according to authoritative state alone.
    /// </summary>
    /// <remarks>
    /// Capability is checked separately by the page. This answers "does the workflow permit it",
    /// never "is this operator allowed" — the API decides that and re-checks every call.
    /// </remarks>
    /// <param name="Preview">The record is in a stage where a preview is meaningful.</param>
    /// <param name="Create">The workflow stage permits creating the Jira issue.</param>
    /// <param name="Retry">Authoritative state says a failed stage can be resumed.</param>
    /// <param name="BlockedReason">Why no action is offered, or <c>null</c> when one is.</param>
    public sealed record Actions(bool Preview, bool Create, bool Retry, string? BlockedReason);

    /// <summary>Ownership as the contract represents it.</summary>
    public enum Ownership
    {
        /// <summary>No live claim.</summary>
        Available,

        /// <summary>A live claim exists. The contract does not say whose — see G-11.</summary>
        Held,

        /// <summary>A claim existed and its lease has run out.</summary>
        Expired
    }

    /// <summary>
    /// Resolves ownership from the claim flag and lease expiry.
    /// </summary>
    /// <param name="record">Authoritative record.</param>
    /// <param name="now">Current time, supplied so this stays testable.</param>
    /// <returns>The ownership state the UI should present.</returns>
    /// <remarks>
    /// <b>"Claimed by me" cannot be determined.</b> <c>OperationalRecordResponse</c> exposes
    /// <c>Claimed</c> as a bare boolean; the owning actor lives on the domain entity but is not
    /// projected (G-11). The UI therefore never claims a record is the current operator's, and
    /// treats any live claim as possibly another operator's. That is the safe direction to be wrong
    /// in: it warns before an action that might collide, rather than implying exclusivity nobody
    /// promised.
    /// </remarks>
    public static Ownership OwnershipOf(OperationalRecordResponse record, DateTimeOffset now)
    {
        if (!record.Claimed)
        {
            return Ownership.Available;
        }

        return record.ClaimExpiresAt is { } expires && expires <= now
            ? Ownership.Expired
            : Ownership.Held;
    }

    /// <summary>Turkish label for a workflow state.</summary>
    /// <param name="state">Workflow state.</param>
    /// <returns>Operator-facing label.</returns>
    public static string StateLabel(OperationalRecordWorkflowState state) => state switch
    {
        OperationalRecordWorkflowState.Imported => "İçe alındı",
        OperationalRecordWorkflowState.Classified => "Sınıflandırıldı",
        OperationalRecordWorkflowState.NeedsManualReview => "Manuel inceleme gerekiyor",
        OperationalRecordWorkflowState.Eligible => "Aktarıma uygun",
        OperationalRecordWorkflowState.Previewed => "Önizlendi",
        OperationalRecordWorkflowState.CreateRequested => "Oluşturma istendi",
        OperationalRecordWorkflowState.CreatingJira => "Jira oluşturuluyor",
        OperationalRecordWorkflowState.JiraCreated => "Jira oluşturuldu",
        OperationalRecordWorkflowState.ClosingOperationalRecord => "Kaynak kapatılıyor",
        OperationalRecordWorkflowState.Completed => "Tamamlandı",
        OperationalRecordWorkflowState.JiraCreateFailed => "Jira oluşturulamadı",
        OperationalRecordWorkflowState.OperationalRecordCloseFailed => "Jira var, kaynak kapatılamadı",
        _ => state.ToString()
    };

    /// <summary>
    /// One sentence of context for a workflow state.
    /// </summary>
    /// <param name="state">Workflow state.</param>
    /// <returns>Explanation, or <c>null</c> when the label says enough.</returns>
    public static string? StateDetail(OperationalRecordWorkflowState state) => state switch
    {
        OperationalRecordWorkflowState.NeedsManualReview =>
            "Onaylı bir kural bu kaydı sınıflandıramadı. Jira aktarımı için sınıflandırma gerekir.",
        OperationalRecordWorkflowState.CreatingJira =>
            "Bir oluşturma denemesi başlatıldı ve sonucu bilinmiyor. Yeni bir Jira kaydı oluşturmak "
            + "mükerrer kayıt riski taşır.",
        OperationalRecordWorkflowState.JiraCreateFailed =>
            "Güvenilir bir Jira anahtarı kaydedilmeden önce oluşturma başarısız oldu.",
        OperationalRecordWorkflowState.OperationalRecordCloseFailed =>
            "Jira kaydı oluşturuldu; yalnızca kaynak kaydın kapatılması tamamlanamadı. Kalan iş "
            + "kaynak tarafındadır — yeni bir Jira kaydı oluşturulmamalıdır.",
        OperationalRecordWorkflowState.Completed =>
            "Jira oluşturuldu ve kaynak kayıt kapatıldı.",
        _ => null
    };

    /// <summary>
    /// Badge tone for a workflow state.
    /// </summary>
    /// <param name="state">Workflow state.</param>
    /// <returns>Tone.</returns>
    /// <remarks>
    /// Only two states are Critical: creation failed outright, and the unknown-outcome stage. A
    /// close failure is Caution rather than Critical on purpose — the Jira issue exists, so the
    /// transfer partly succeeded, and painting it as an error invites someone to "fix" it by
    /// creating a second issue. Red stays for conditions that are actually critical.
    /// </remarks>
    public static SoStatusBadge.BadgeTone StateTone(OperationalRecordWorkflowState state) => state switch
    {
        OperationalRecordWorkflowState.Completed => SoStatusBadge.BadgeTone.Positive,
        OperationalRecordWorkflowState.JiraCreated => SoStatusBadge.BadgeTone.Positive,
        OperationalRecordWorkflowState.JiraCreateFailed => SoStatusBadge.BadgeTone.Critical,
        OperationalRecordWorkflowState.CreatingJira => SoStatusBadge.BadgeTone.Critical,
        OperationalRecordWorkflowState.OperationalRecordCloseFailed => SoStatusBadge.BadgeTone.Caution,
        OperationalRecordWorkflowState.NeedsManualReview => SoStatusBadge.BadgeTone.Caution,
        OperationalRecordWorkflowState.ClosingOperationalRecord => SoStatusBadge.BadgeTone.Info,
        OperationalRecordWorkflowState.CreateRequested => SoStatusBadge.BadgeTone.Info,
        OperationalRecordWorkflowState.Eligible => SoStatusBadge.BadgeTone.Info,
        OperationalRecordWorkflowState.Previewed => SoStatusBadge.BadgeTone.Info,
        _ => SoStatusBadge.BadgeTone.Neutral
    };

    /// <summary>Icon reinforcing a workflow state, so state is not carried by colour alone.</summary>
    /// <param name="state">Workflow state.</param>
    /// <returns>Material icon name.</returns>
    public static string StateIcon(OperationalRecordWorkflowState state) => state switch
    {
        OperationalRecordWorkflowState.Completed => Icons.Material.Filled.TaskAlt,
        OperationalRecordWorkflowState.JiraCreated => Icons.Material.Filled.CheckCircle,
        OperationalRecordWorkflowState.JiraCreateFailed => Icons.Material.Filled.ErrorOutline,
        OperationalRecordWorkflowState.CreatingJira => Icons.Material.Filled.HelpOutline,
        OperationalRecordWorkflowState.OperationalRecordCloseFailed => Icons.Material.Filled.LinkOff,
        OperationalRecordWorkflowState.NeedsManualReview => Icons.Material.Filled.RuleFolder,
        OperationalRecordWorkflowState.ClosingOperationalRecord => Icons.Material.Filled.Sync,
        OperationalRecordWorkflowState.Previewed => Icons.Material.Filled.Preview,
        _ => Icons.Material.Filled.Circle
    };

    /// <summary>
    /// Whether a Jira issue is known to exist for this record.
    /// </summary>
    /// <param name="record">Authoritative record.</param>
    /// <returns><c>true</c> when an issue key is persisted.</returns>
    public static bool HasJira(OperationalRecordResponse record) =>
        !string.IsNullOrWhiteSpace(record.JiraIssueKey);

    /// <summary>
    /// Whether the outcome of a Jira create attempt is unknown.
    /// </summary>
    /// <param name="record">Authoritative record.</param>
    /// <returns><c>true</c> while an attempt is unresolved.</returns>
    /// <remarks>
    /// The record carries no explicit <c>ReconciliationRequired</c> flag (G-12); the closest
    /// authoritative signal a read gives is the <c>CreatingJira</c> stage with no issue key. The
    /// definitive answer arrives as <c>WorkflowConflict</c> with <c>stage: "jira-reconciliation"</c>
    /// when an action is attempted.
    /// </remarks>
    public static bool OutcomeUnknown(OperationalRecordResponse record) =>
        record.WorkflowState == OperationalRecordWorkflowState.CreatingJira && !HasJira(record);

    /// <summary>
    /// Resolves what the workflow currently permits.
    /// </summary>
    /// <param name="record">Authoritative record.</param>
    /// <returns>Permitted actions and, when none, why.</returns>
    public static Actions ActionsFor(OperationalRecordResponse record)
    {
        if (!record.JiraEligible)
        {
            return new Actions(false, false, false, "Bu kayıt Jira aktarımına uygun değil.");
        }

        // Checked before any per-state rule, and deliberately not folded into them. An existing
        // issue key is the single fact that makes creating unsafe, and it must hold even if a
        // record turns up in a stage that would otherwise permit it — a partially rolled-back
        // retry, a source re-import, or a state added later. Leaving this to each case means one
        // missed case is a duplicate Jira issue. A unit test asserts it across every state.
        bool hasJira = HasJira(record);

        return record.WorkflowState switch
        {
            // Terminal success. Nothing to do, and nothing that could be mistaken for something to do.
            OperationalRecordWorkflowState.Completed =>
                new Actions(false, false, false, "Aktarım tamamlandı."),

            // A Jira issue exists. Only the source side is outstanding, and only retry may touch it.
            OperationalRecordWorkflowState.OperationalRecordCloseFailed =>
                new Actions(false, false, true, null),
            OperationalRecordWorkflowState.JiraCreated =>
                new Actions(false, false, true, null),
            OperationalRecordWorkflowState.ClosingOperationalRecord =>
                new Actions(false, false, false, "Kaynak kapatma aşaması sürüyor."),

            // Failed before any trusted key was stored, so resuming is the safe move — not a new create.
            OperationalRecordWorkflowState.JiraCreateFailed =>
                new Actions(false, false, true, null),

            // Unknown outcome. Deliberately offers nothing: see StateDetail.
            OperationalRecordWorkflowState.CreatingJira =>
                new Actions(false, false, false, "Önceki denemenin sonucu doğrulanmalı."),
            OperationalRecordWorkflowState.CreateRequested =>
                new Actions(false, false, false, "Oluşturma isteği işleniyor."),

            OperationalRecordWorkflowState.Eligible or OperationalRecordWorkflowState.Previewed =>
                hasJira
                    ? new Actions(false, false, true, "Bu kayıt için zaten bir Jira kaydı var.")
                    : new Actions(true, true, false, null),

            OperationalRecordWorkflowState.NeedsManualReview =>
                new Actions(false, false, false, "Sınıflandırma tamamlanmadan aktarım yapılamaz."),

            // Imported/Classified: classification has not yet marked it eligible.
            _ => new Actions(false, false, false, "Kayıt henüz aktarıma hazır değil.")
        };
    }

    /// <summary>
    /// Whether the record needs an operator to look at it.
    /// </summary>
    /// <param name="record">Authoritative record.</param>
    /// <returns><c>true</c> when the record is stuck or its outcome is unresolved.</returns>
    public static bool NeedsAttention(OperationalRecordResponse record) =>
        record.WorkflowState is OperationalRecordWorkflowState.JiraCreateFailed
            or OperationalRecordWorkflowState.OperationalRecordCloseFailed
            or OperationalRecordWorkflowState.CreatingJira
            or OperationalRecordWorkflowState.NeedsManualReview;
}
