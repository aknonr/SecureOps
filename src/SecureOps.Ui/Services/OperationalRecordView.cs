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
    /// <summary>Describes authoritative uncertainty before the persisted workflow stage.</summary>
    public static string StateLabel(OperationalRecordResponse record) =>
        OutcomeUnknown(record) ? "Jira sonucu belirsiz" : StateLabel(record.WorkflowState);

    /// <summary>Uncertain outcomes need reconciliation, not a definitive failure indicator.</summary>
    public static SoStatusBadge.BadgeTone StateTone(OperationalRecordResponse record) =>
        OutcomeUnknown(record) ? SoStatusBadge.BadgeTone.Caution : StateTone(record.WorkflowState);

    /// <summary>Provides a matching non-colour signal for uncertain outcomes.</summary>
    public static string StateIcon(OperationalRecordResponse record) =>
        OutcomeUnknown(record) ? Icons.Material.Filled.HelpOutline : StateIcon(record.WorkflowState);

    /// <summary>Does not describe an unconfirmed publication as a known failure.</summary>
    public static string? StateDetail(OperationalRecordResponse record) =>
        OutcomeUnknown(record) ? "Jira sonucu doğrulanamadı. Yeni kayıt oluşturmadan önce mutabakat gerekir."
            : StateDetail(record.WorkflowState);

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
    /// <param name="WriteFenceReason">
    /// Why writes specifically are unavailable while reads still are, or <c>null</c> when no fence
    /// applies.
    /// </param>
    /// <remarks>
    /// <see cref="WriteFenceReason"/> is deliberately separate from <see cref="BlockedReason"/>.
    /// A blocked reason explains a workflow stage that offers nothing; a write fence explains an
    /// environment where reading and previewing still work and only the external write is closed.
    /// Collapsing them would tell an operator the record is not ready when in fact it is, and the
    /// environment is what stops them.
    /// </remarks>
    public sealed record Actions(
        bool Preview,
        bool Create,
        bool Retry,
        string? BlockedReason,
        string? WriteFenceReason = null);

    /// <summary>Explains a disabled write while the record itself is otherwise actionable.</summary>
    public const string WriteFenceHelp =
        "Gerçek veri read-only TEST modunda kullanılıyor. Dış sistemlere yazma işlemleri kapalıdır.";

    /// <summary>Shown when the workflow stalled but the server does not permit resuming it.</summary>
    private const string RetryBlocked =
        "Sunucu bu kayıt için yeniden denemeye izin vermiyor.";

    /// <summary>Ownership as the contract represents it.</summary>
    public enum Ownership
    {
        /// <summary>No live claim, and no record of a previous one.</summary>
        Available,

        /// <summary>A live claim held by the signed-in operator.</summary>
        Mine,

        /// <summary>A live claim held by somebody else.</summary>
        Other,

        /// <summary>
        /// No live claim, but the record still carries who held the last one.
        /// </summary>
        /// <remarks>
        /// This is metadata about the past, not current ownership. It is a distinct state so that
        /// a lapsed claim can be shown as history without ever reading as "someone owns this".
        /// </remarks>
        Lapsed
    }

    /// <summary>
    /// Resolves ownership from the authoritative claim fields.
    /// </summary>
    /// <param name="record">Authoritative record.</param>
    /// <param name="now">Current time, supplied so this stays testable.</param>
    /// <param name="currentActor">
    /// The signed-in operator as the <i>API</i> names them — <c>GET /identity/me</c>'s <c>name</c>,
    /// not the browser cookie's. The two differ (the API sees <c>demo:platform-admin</c> where the
    /// cookie says <c>platform-admin</c>), and comparing the wrong one would report every record as
    /// someone else's.
    /// </param>
    /// <returns>The ownership state the UI should present.</returns>
    /// <remarks>
    /// <b><c>Claimed</c> is the only signal that a claim is live.</b> An expired claim may keep its
    /// <c>ClaimedBy</c> metadata while <c>Claimed</c> is <c>false</c>, so deciding ownership from
    /// <c>ClaimedBy</c> alone would show a long-lapsed claim as active and stop operators working a
    /// record nobody holds. The flag is therefore checked first, and <c>ClaimedBy</c> only
    /// distinguishes <i>whose</i> live claim it is.
    /// </remarks>
    public static Ownership OwnershipOf(
        OperationalRecordResponse record,
        DateTimeOffset now,
        string? currentActor)
    {
        if (!record.Claimed)
        {
            // Not owned. ClaimedBy may still be populated; that is history, not ownership.
            return string.IsNullOrWhiteSpace(record.ClaimedBy) ? Ownership.Available : Ownership.Lapsed;
        }

        // Defensive: a live flag whose lease has visibly run out is treated as lapsed rather than
        // as somebody's, so a stuck flag cannot block the queue indefinitely.
        if (record.ClaimExpiresAt is { } expires && expires <= now)
        {
            return Ownership.Lapsed;
        }

        if (string.IsNullOrWhiteSpace(currentActor) || string.IsNullOrWhiteSpace(record.ClaimedBy))
        {
            // Live claim, owner unknown to us. Assume it is not ours: warning before a possible
            // collision is the safe direction to be wrong in.
            return Ownership.Other;
        }

        return string.Equals(record.ClaimedBy, currentActor, StringComparison.OrdinalIgnoreCase)
            ? Ownership.Mine
            : Ownership.Other;
    }

    /// <summary>
    /// Operator-facing label for the backend's stable presentation category.
    /// </summary>
    /// <param name="presentationState">The contract's <c>presentationState</c> value.</param>
    /// <returns>The documented Turkish label for that category.</returns>
    /// <remarks>
    /// Mapped from the stable category code and from nothing else. The English descriptions and the
    /// eligibility prose that travel alongside it are presentation details on the server's side of
    /// the contract; keying a label off them would make a backend reword silently change what the
    /// operator is told.
    /// <para>
    /// An unrecognised value is reported as unknown rather than folded into one of the four. A
    /// category this UI has never seen is a contract gap, and quietly labelling it "İnceleme
    /// Gerekiyor" would hide that while asserting something about the record that may be false.
    /// </para>
    /// </remarks>
    public static string PresentationLabel(string? presentationState) => presentationState switch
    {
        OperationalRecordPresentationStates.NeedsAttention => "İnceleme Gerekiyor",
        OperationalRecordPresentationStates.Actionable => "Jira'ya Aktarılabilir",
        OperationalRecordPresentationStates.InProgress => "İşlemde",
        OperationalRecordPresentationStates.Completed => "Tamamlandı",
        _ => "Durum bilinmiyor"
    };

    /// <summary>Badge tone for a presentation category.</summary>
    /// <param name="presentationState">The contract's <c>presentationState</c> value.</param>
    /// <returns>Tone.</returns>
    public static SoStatusBadge.BadgeTone PresentationTone(string? presentationState) => presentationState switch
    {
        OperationalRecordPresentationStates.NeedsAttention => SoStatusBadge.BadgeTone.Caution,
        OperationalRecordPresentationStates.Actionable => SoStatusBadge.BadgeTone.Info,
        OperationalRecordPresentationStates.InProgress => SoStatusBadge.BadgeTone.Info,
        OperationalRecordPresentationStates.Completed => SoStatusBadge.BadgeTone.Positive,
        _ => SoStatusBadge.BadgeTone.Neutral
    };

    /// <summary>Icon for a presentation category, so the category is not carried by colour alone.</summary>
    /// <param name="presentationState">The contract's <c>presentationState</c> value.</param>
    /// <returns>Material icon name.</returns>
    public static string PresentationIcon(string? presentationState) => presentationState switch
    {
        OperationalRecordPresentationStates.NeedsAttention => Icons.Material.Filled.ReportProblem,
        OperationalRecordPresentationStates.Actionable => Icons.Material.Filled.PlayCircleOutline,
        OperationalRecordPresentationStates.InProgress => Icons.Material.Filled.Sync,
        OperationalRecordPresentationStates.Completed => Icons.Material.Filled.TaskAlt,
        _ => Icons.Material.Filled.HelpOutline
    };

    /// <summary>
    /// The operator's path through a transfer, in the order they walk it.
    /// </summary>
    /// <remarks>
    /// Named for what the operator does, not for what the workflow is called internally. The steps
    /// exist so somebody who is not a developer can see what has already happened and what the next
    /// button will actually do.
    /// </remarks>
    public static readonly IReadOnlyList<string> OperatorSteps =
    [
        "Kaydı İncele",
        "Jira Taslağını Önizle",
        "Bilgileri Doğrula",
        "Jira Kaydı Oluştur",
        "Kaynak Kaydı Tamamla"
    ];

    /// <summary>
    /// Which operator step a record is currently on.
    /// </summary>
    /// <param name="record">Authoritative record.</param>
    /// <returns>Zero-based index into <see cref="OperatorSteps"/>; the count when finished.</returns>
    /// <remarks>
    /// Derived from durable workflow state, never from what the operator last clicked. A record that
    /// is mid-create after a browser refresh must still show as mid-create.
    /// </remarks>
    public static int CurrentStep(OperationalRecordResponse record) => record.WorkflowState switch
    {
        OperationalRecordWorkflowState.Completed => OperatorSteps.Count,

        OperationalRecordWorkflowState.JiraCreated
            or OperationalRecordWorkflowState.ClosingOperationalRecord
            or OperationalRecordWorkflowState.OperationalRecordCloseFailed => 4,

        OperationalRecordWorkflowState.CreateRequested
            or OperationalRecordWorkflowState.CreatingJira
            or OperationalRecordWorkflowState.JiraCreateFailed => 3,

        OperationalRecordWorkflowState.Previewed => 2,

        _ => 0
    };

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
            "Jira isteğinin sonucu kesin olarak doğrulanamadı. Yeni kayıt oluşturmadan önce "
            + "uzlaştırma gereklidir.",
        OperationalRecordWorkflowState.JiraCreateFailed =>
            "Güvenilir bir Jira anahtarı kaydedilmeden önce oluşturma başarısız oldu.",
        OperationalRecordWorkflowState.OperationalRecordCloseFailed =>
            "Jira kaydı oluşturuldu ancak kaynak kayıt tamamlanamadı. Jira tekrar oluşturulmadan "
            + "kaynak tamamlama işlemi yeniden denenebilir.",
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
        record.JiraExists || !string.IsNullOrWhiteSpace(record.JiraIssueKey);

    /// <summary>
    /// Whether the outcome of a Jira create attempt is unknown.
    /// </summary>
    /// <param name="record">Authoritative record.</param>
    /// <returns><c>true</c> while an attempt is unresolved.</returns>
    /// <remarks>
    /// Reads the authoritative <c>ReconciliationRequired</c> flag. The <c>CreatingJira</c> stage
    /// without an issue key is kept as a secondary signal: it is the same situation mid-flight, and
    /// treating it as resolved merely because the flag has not been set yet would offer actions
    /// during the window the flag exists to close.
    /// </remarks>
    public static bool OutcomeUnknown(OperationalRecordResponse record) =>
        record.ReconciliationRequired
        || (record.WorkflowState == OperationalRecordWorkflowState.CreatingJira && !HasJira(record));

    /// <summary>
    /// Resolves what the workflow currently permits.
    /// </summary>
    /// <param name="record">Authoritative record.</param>
    /// <returns>Permitted actions and, when none, why.</returns>
    public static Actions ActionsFor(OperationalRecordResponse record) =>
        ApplyWriteFence(WorkflowActionsFor(record), record);

    // The environment fence is applied last, over whatever the workflow would otherwise allow.
    // Placing it here rather than inside each state means a state added later cannot accidentally
    // offer a write the environment forbids — the server would reject it anyway, but offering a
    // button that can only fail is how an operator learns to distrust the screen.
    //
    // Preview is deliberately untouched: reads and the Jira draft still work in this mode, and that
    // is most of what the record screen is for.
    private static Actions ApplyWriteFence(Actions actions, OperationalRecordResponse record) =>
        record.ReadOnlyIntegrationMode
            ? actions with { Create = false, Retry = false, WriteFenceReason = WriteFenceHelp }
            : actions;

    private static Actions WorkflowActionsFor(OperationalRecordResponse record)
    {
        if (!record.JiraEligible)
        {
            return new Actions(false, false, false, "Bu kayıt Jira aktarımına uygun değil.");
        }

        // Checked before any per-state rule, and deliberately not folded into them. An existing
        // issue makes creating unsafe, and that must hold even if a record turns up in a stage
        // that would otherwise permit it — a partially rolled-back retry, a source re-import, or a
        // state added later. Leaving this to each case means one missed case is a duplicate Jira
        // issue. A unit test asserts it across every state.
        bool hasJira = HasJira(record);

        // Reconciliation outranks the state machine. While the outcome of a create is unresolved,
        // neither creating nor resuming is safe, whatever stage the record reports.
        if (record.ReconciliationRequired)
        {
            return new Actions(
                false,
                false,
                // Only if the server explicitly says so — never inferred.
                record.RetryEligible,
                record.RetryEligible ? null : "Önceki denemenin sonucu doğrulanmalı.");
        }

        return record.WorkflowState switch
        {
            // Terminal success. Nothing to do, and nothing that could be mistaken for something to do.
            OperationalRecordWorkflowState.Completed =>
                new Actions(false, false, false, "Aktarım tamamlandı."),

            // A Jira issue exists. Only the source side is outstanding, and only retry may touch it.
            OperationalRecordWorkflowState.OperationalRecordCloseFailed =>
                new Actions(false, false, record.RetryEligible, record.RetryEligible ? null : RetryBlocked),
            OperationalRecordWorkflowState.JiraCreated =>
                new Actions(false, false, record.RetryEligible, record.RetryEligible ? null : RetryBlocked),
            OperationalRecordWorkflowState.ClosingOperationalRecord =>
                new Actions(false, false, false, "Kaynak kapatma aşaması sürüyor."),

            // Failed before any trusted key was stored, so resuming is the safe move — not a new create.
            OperationalRecordWorkflowState.JiraCreateFailed =>
                new Actions(false, false, record.RetryEligible, record.RetryEligible ? null : RetryBlocked),

            // Unknown outcome. Deliberately offers nothing: see StateDetail.
            OperationalRecordWorkflowState.CreatingJira =>
                new Actions(false, false, false, "Önceki denemenin sonucu doğrulanmalı."),
            OperationalRecordWorkflowState.CreateRequested =>
                new Actions(false, false, false, "Oluşturma isteği işleniyor."),

            OperationalRecordWorkflowState.Eligible or OperationalRecordWorkflowState.Previewed =>
                hasJira
                    ? new Actions(false, false, record.RetryEligible, "Bu kayıt için zaten bir Jira kaydı var.")
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
        record.ReconciliationRequired
        || record.WorkflowState is OperationalRecordWorkflowState.JiraCreateFailed
            or OperationalRecordWorkflowState.OperationalRecordCloseFailed
            or OperationalRecordWorkflowState.CreatingJira
            or OperationalRecordWorkflowState.NeedsManualReview;
}
