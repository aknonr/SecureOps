using System.Globalization;
using SecureOps.Shared.Audit;
using SecureOps.Shared.Contracts.Reporting;

namespace SecureOps.Ui.Services;

/// <summary>
/// Presentation rules for the management report.
/// </summary>
/// <remarks>
/// <para>
/// This type reshapes and labels what the API returned. It computes no metric: no rates, no
/// projections, no totals the server did not already send. That restriction is the point of
/// ADR-0011 — every figure on the dashboard must be explainable from persisted backend evidence,
/// and a number the browser derived would have no audit trail behind it.
/// </para>
/// <para>
/// The one thing it does decide is <i>availability</i>. The contract expresses "we cannot measure
/// this" in two ways — a null count, and a duration with no samples — and both must render as
/// unavailable rather than as zero. Zero and unmeasured mean opposite things to a manager reading
/// the page: one says the condition did not occur, the other says nobody knows.
/// </para>
/// </remarks>
public static class ManagementReportView
{
    private static readonly CultureInfo _tr = CultureInfo.GetCultureInfo("tr-TR");

    /// <summary>Shown wherever the backend reports a metric it cannot measure.</summary>
    public const string UnavailableText = "Yeterli geçmiş veri yok";

    /// <summary>How urgently an attention area should be read.</summary>
    public enum AttentionTone
    {
        /// <summary>A guard engaged as designed; informational, not a fault.</summary>
        Guarded,

        /// <summary>Worth reviewing but not blocking.</summary>
        Notable,

        /// <summary>Needs someone to act.</summary>
        Urgent
    }

    /// <summary>
    /// One operational condition worth a manager's attention, taken directly from a backend count.
    /// </summary>
    /// <param name="Title">What the condition is.</param>
    /// <param name="Count">The backend count that raised it.</param>
    /// <param name="Detail">What it means and what to do about it.</param>
    /// <param name="Tone">How urgently to read it.</param>
    public sealed record AttentionItem(string Title, long Count, string Detail, AttentionTone Tone);

    /// <summary>
    /// One elapsed-duration statistic prepared for display.
    /// </summary>
    /// <param name="Label">Short Turkish name of the interval.</param>
    /// <param name="Definition">The server's own definition, shown verbatim as the tooltip.</param>
    /// <param name="SampleCount">How many workflows the figures are based on.</param>
    /// <param name="Minimum">Formatted minimum, or <c>null</c> when unmeasured.</param>
    /// <param name="Average">Formatted average, or <c>null</c> when unmeasured.</param>
    /// <param name="Maximum">Formatted maximum, or <c>null</c> when unmeasured.</param>
    public sealed record DurationView(
        string Label,
        string Definition,
        long SampleCount,
        string? Minimum,
        string? Average,
        string? Maximum)
    {
        /// <summary>Whether any workflow in the window produced a measurable elapsed time.</summary>
        public bool HasSamples => SampleCount > 0 && Average is not null;
    }

    /// <summary>
    /// Formats a count for display.
    /// </summary>
    /// <param name="value">Backend count.</param>
    /// <returns>Grouped Turkish numeral.</returns>
    public static string Count(long value) => value.ToString("N0", _tr);

    /// <summary>
    /// Formats a nullable count, treating null as unmeasured rather than as zero.
    /// </summary>
    /// <param name="value">Backend count, or <c>null</c> when the backend cannot measure it.</param>
    /// <returns>Grouped numeral, or the unavailable text.</returns>
    /// <remarks>
    /// <c>rateLimitEvents</c> is the only nullable count in the v1 reporting contract. It is null
    /// because rate-limit rejections are not audited yet, which is a documented limitation and not
    /// an absence of rejections.
    /// </remarks>
    public static string Count(long? value) => value is null ? UnavailableText : Count(value.Value);

    /// <summary>
    /// Whether the window contains any persisted evidence at all.
    /// </summary>
    /// <param name="report">Report to inspect.</param>
    /// <returns><c>true</c> when at least one counted event or record exists.</returns>
    /// <remarks>
    /// Used to replace a full page of zeroes with a single honest statement. A wall of zeroes reads
    /// as a measurement; "no persisted evidence in this window" reads as what it is, which matters
    /// most on a pilot whose SQL history starts part-way through the range being asked for.
    /// </remarks>
    public static bool HasEvidence(ManagementReportResponse report) =>
        report.IdentityLookup.TotalLookups > 0
        || report.OperationalWorkflow.RecordsImported > 0
        || report.OperationalWorkflow.Eligible > 0
        || report.OperationalWorkflow.Previewed > 0
        || report.OperationalWorkflow.JiraCreated > 0
        || report.OperationalWorkflow.Completed > 0
        || report.OperationalWorkflow.Failures > 0
        || report.OperationalWorkflow.ReconciliationRequired > 0
        || report.OperationalWorkflow.SourceChangedPrevented > 0
        || report.OperationalWorkflow.ClosedOrMissingPrevented > 0
        || report.OperationalWorkflow.DuplicateCreatePrevented > 0
        || report.OperationalWorkflow.Retries.Requested > 0
        || report.PlatformAdoption.UniqueActiveUsersInWindow > 0
        || report.PlatformAdoption.OperationsByWorkflow.Any(item => item.Count > 0)
        || report.PlatformAdoption.AccessRequestActivity.Any(item => item.Count > 0)
        || report.SecurityAndQuality.AuthorizationFailures > 0
        || report.SecurityAndQuality.ConcurrencyConflicts > 0
        || report.SecurityAndQuality.ReconciliationEvents > 0
        || report.SecurityAndQuality.ProviderUnavailableEvents > 0;

    /// <summary>
    /// Whether the identity trend carries at least one non-zero day.
    /// </summary>
    /// <param name="report">Report to inspect.</param>
    /// <returns><c>true</c> when the chart would show something.</returns>
    public static bool HasTrend(ManagementReportResponse report) =>
        report.IdentityLookup.Trend.Any(point => point.Total > 0);

    /// <summary>
    /// Prepares the elapsed-duration statistics for display.
    /// </summary>
    /// <param name="report">Report to project.</param>
    /// <returns>One view per duration the server defines, in the server's order.</returns>
    public static IReadOnlyList<DurationView> Durations(ManagementReportResponse report) =>
        report.OperationalWorkflow.Durations.Select(Duration).ToArray();

    /// <summary>
    /// Prepares one duration statistic.
    /// </summary>
    /// <param name="duration">Server statistic.</param>
    /// <returns>Labelled, formatted view.</returns>
    public static DurationView Duration(DurationStatisticsResponse duration) => new(
        DurationLabel(duration.Definition),
        duration.Definition,
        duration.SampleCount,
        FormatSeconds(duration.MinimumSeconds),
        FormatSeconds(duration.AverageSeconds),
        FormatSeconds(duration.MaximumSeconds));

    /// <summary>
    /// Formats an elapsed duration in seconds.
    /// </summary>
    /// <param name="seconds">Elapsed seconds, or <c>null</c> when unmeasured.</param>
    /// <returns>Human-readable duration, or <c>null</c> when unmeasured.</returns>
    /// <remarks>
    /// Precision drops as the magnitude grows: minutes stop mattering once a workflow has taken
    /// days, and showing them implies the measurement is tighter than it is.
    /// </remarks>
    public static string? FormatSeconds(double? seconds)
    {
        if (seconds is null || double.IsNaN(seconds.Value) || seconds.Value < 0)
        {
            return null;
        }

        long total = (long)Math.Round(seconds.Value, MidpointRounding.AwayFromZero);

        if (total < 60)
        {
            return $"{total} sn";
        }

        if (total < 3600)
        {
            long minutes = total / 60;
            long remainder = total % 60;
            return remainder == 0 ? $"{minutes} dk" : $"{minutes} dk {remainder} sn";
        }

        if (total < 86400)
        {
            long hours = total / 3600;
            long minutes = total % 3600 / 60;
            return minutes == 0 ? $"{hours} sa" : $"{hours} sa {minutes} dk";
        }

        long days = total / 86400;
        long spareHours = total % 86400 / 3600;
        return spareHours == 0 ? $"{days} gün" : $"{days} gün {spareHours} sa";
    }

    /// <summary>
    /// Turkish label for a server duration definition.
    /// </summary>
    /// <param name="definition">Definition sentence as the API returned it.</param>
    /// <returns>Short label, or the definition itself when it is not one of the known three.</returns>
    /// <remarks>
    /// The v1 contract carries no stable key for a duration — only this English definition sentence
    /// and the array order. Matching on the sentence is therefore the most specific hook available;
    /// an unrecognised definition falls through to its own text so a new interval added server-side
    /// still renders truthfully instead of being mislabelled as one of these.
    /// </remarks>
    public static string DurationLabel(string definition) => definition switch
    {
        "First persisted import to first persisted preview" => "İçe alma → önizleme",
        "Workflow claim to durable Jira issue-key persistence" => "Devralma → Jira kaydı",
        "Workflow claim to durable workflow completion" => "Devralma → tamamlanma",
        _ => definition
    };

    /// <summary>
    /// Turkish rendering of one reported data limitation.
    /// </summary>
    /// <param name="limitation">Limitation sentence as the API returned it.</param>
    /// <returns>Reviewed Turkish translation, or the server text verbatim when unrecognised.</returns>
    /// <remarks>
    /// <para>
    /// These sentences are the only part of the report written as prose, and they carry no code —
    /// matching on the exact English text is the only hook the v1 contract offers. Each translation
    /// below is a reviewed rendering of one specific sentence, not a paraphrase: a limitation that
    /// gets softened in translation stops doing its job, which is to stop a reader treating an
    /// unmeasured zero as a measured one.
    /// </para>
    /// <para>
    /// Anything the server sends that is not on this list is shown exactly as received. That is the
    /// safe direction to fail: an English sentence on a Turkish screen is a blemish, whereas a
    /// silently dropped or mistranslated limitation is a false statement about the data.
    /// </para>
    /// </remarks>
    public static string LimitationLabel(string limitation) => limitation switch
    {
        "Rate-limit rejections are not currently persisted as audit events; this metric is unavailable."
            => "Hız sınırı reddi şu anda denetim kaydına yazılmıyor; bu ölçüm kullanılamıyor.",

        "Historical access-version conflicts and Operational Record source-query outages are unavailable when no audit event exists."
            => "Geçmiş erişim sürüm çakışmaları ve operasyonel kayıt kaynak sorgusu kesintileri, "
               + "denetim kaydı bulunmadığında ölçülemez.",

        "Invalid items skipped inside historical bulk identity requests do not have individual terminal audit rows."
            => "Geçmiş toplu kimlik isteklerinde atlanan geçersiz kayıtların ayrı sonuç denetim satırı yoktur.",

        "Duplicate-create prevention is measurable only from the first release that writes its explicit audit event."
            => "Mükerrer oluşturma engelleme, bu olayı denetim kaydına yazan ilk sürümden itibaren ölçülebilir.",

        "Elapsed durations include waits and retries and are not active labor, time saved, or operator performance."
            => "Geçen süreler bekleme ve yeniden denemeleri içerir; aktif çalışma süresi, kazanılan zaman "
               + "veya operatör performansı değildir.",

        _ => limitation
    };

    /// <summary>
    /// Turkish label for a server workflow category.
    /// </summary>
    /// <param name="name">Category name from the API.</param>
    /// <returns>Operator-facing label.</returns>
    public static string WorkflowLabel(string name) => name switch
    {
        "IdentityLookup" => "Kimlik sorgulama",
        "Access" => "Erişim yönetimi",
        "OperationalRecordJira" => "Operasyonel kayıt → Jira",
        _ => name
    };

    /// <summary>
    /// Turkish label for an access lifecycle action.
    /// </summary>
    /// <param name="action">Audit action name from the API.</param>
    /// <returns>Operator-facing label.</returns>
    public static string AccessActionLabel(string action) => action switch
    {
        AuditActions.AccessRequested => "Erişim talebi",
        AuditActions.AccessApproved => "Onaylandı",
        AuditActions.AccessRejected => "Reddedildi",
        AuditActions.AccessDisabled => "Devre dışı bırakıldı",
        AuditActions.RoleAssigned => "Rol atandı",
        AuditActions.RoleRemoved => "Rol kaldırıldı",
        _ => action
    };

    /// <summary>
    /// Collects the conditions in this window that a manager should look at.
    /// </summary>
    /// <param name="report">Report to inspect.</param>
    /// <returns>Attention areas, most urgent first; empty when nothing needs review.</returns>
    /// <remarks>
    /// Every item is one backend count with a threshold of "greater than zero". Nothing is scored,
    /// weighted, or combined — a manager who clicks through to the underlying screen must find
    /// exactly the number quoted here.
    /// <para>
    /// Prevention counts are included but toned as <see cref="AttentionTone.Guarded"/>. A duplicate
    /// create that was stopped, or a changed source record that blocked a transfer, is a safety
    /// mechanism working. Presenting those in the same register as failures would teach the reader
    /// to treat a correct outcome as an incident.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<AttentionItem> Attention(ManagementReportResponse report)
    {
        List<AttentionItem> items = [];
        OperationalWorkflowMetricsResponse workflow = report.OperationalWorkflow;
        SecurityQualityMetricsResponse security = report.SecurityAndQuality;

        if (workflow.ReconciliationRequired > 0)
        {
            items.Add(new AttentionItem(
                "Mutabakat bekleyen kayıt",
                workflow.ReconciliationRequired,
                "Jira oluşturma sonucu doğrulanamadığı için otomatik yeniden deneme durduruldu. "
                + "Bu kayıtlar elle kontrol edilmeden yeni Jira kaydı oluşturulmamalıdır.",
                AttentionTone.Urgent));
        }

        if (workflow.Failures > 0)
        {
            items.Add(new AttentionItem(
                "Başarısız aktarım adımı",
                workflow.Failures,
                "Jira oluşturma veya kaynak kayıt kapatma adımı tamamlanamadı.",
                AttentionTone.Urgent));
        }

        if (workflow.Retries.UnresolvedAtWindowEnd > 0)
        {
            items.Add(new AttentionItem(
                "Sonucu netleşmemiş yeniden deneme",
                workflow.Retries.UnresolvedAtWindowEnd,
                "Aralık sonunda bu yeniden denemeler için başarı ya da başarısızlık kaydı yoktu. "
                + "Sonraki aralıkta sonuçlanmış olabilirler.",
                AttentionTone.Notable));
        }

        if (security.ProviderUnavailableEvents > 0)
        {
            items.Add(new AttentionItem(
                "Dizin sağlayıcısı erişilemedi",
                security.ProviderUnavailableEvents,
                "Kimlik sorgulaması sağlayıcı hatası veya zaman aşımı ile sonuçlandı.",
                AttentionTone.Notable));
        }

        if (security.AuthorizationFailures > 0)
        {
            items.Add(new AttentionItem(
                "Yetkisiz erişim denemesi",
                security.AuthorizationFailures,
                "Yetki kontrolü tarafından reddedilen istekler. Eksik yetki tanımının da göstergesi olabilir.",
                AttentionTone.Notable));
        }

        if (security.ConcurrencyConflicts > 0)
        {
            items.Add(new AttentionItem(
                "Eşzamanlılık çakışması",
                security.ConcurrencyConflicts,
                "Aynı kayıt üzerinde iki operatör aynı anda çalıştı; işlem güvenli şekilde durduruldu.",
                AttentionTone.Notable));
        }

        if (workflow.SourceChangedPrevented > 0)
        {
            items.Add(new AttentionItem(
                "Kaynak değiştiği için durduruldu",
                workflow.SourceChangedPrevented,
                "Kaynak kayıt SecureOps'a alındıktan sonra değişti ve aktarım engellendi. Koruma çalıştı.",
                AttentionTone.Guarded));
        }

        if (workflow.ClosedOrMissingPrevented > 0)
        {
            items.Add(new AttentionItem(
                "Kaynak kapalı olduğu için durduruldu",
                workflow.ClosedOrMissingPrevented,
                "Kapalı veya bulunamayan kaynak kayıt için Jira oluşturulmadı. Koruma çalıştı.",
                AttentionTone.Guarded));
        }

        if (workflow.DuplicateCreatePrevented > 0)
        {
            items.Add(new AttentionItem(
                "Mükerrer Jira kaydı önlendi",
                workflow.DuplicateCreatePrevented,
                "Tekrarlanan oluşturma isteği Jira'ya gitmeden durduruldu. Koruma çalıştı.",
                AttentionTone.Guarded));
        }

        return items;
    }
}
