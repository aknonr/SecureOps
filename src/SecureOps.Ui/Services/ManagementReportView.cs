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
    /// <param name="Key">Stable server key that identifies the interval.</param>
    /// <param name="Label">Short Turkish name of the interval.</param>
    /// <param name="Definition">The server's own definition, shown verbatim as the tooltip.</param>
    /// <param name="SampleCount">How many workflows the figures are based on.</param>
    /// <param name="Minimum">Formatted minimum, or <c>null</c> when unmeasured.</param>
    /// <param name="Average">Formatted average, or <c>null</c> when unmeasured.</param>
    /// <param name="Maximum">Formatted maximum, or <c>null</c> when unmeasured.</param>
    public sealed record DurationView(
        string Key,
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
    /// How much of the requested window persisted evidence actually covers.
    /// </summary>
    public enum CoverageState
    {
        /// <summary>Evidence begins at or before the requested start; a zero is a measured zero.</summary>
        Complete,

        /// <summary>Evidence begins inside the window; the earlier part is unmeasured, not empty.</summary>
        Partial,

        /// <summary>No persisted reportable evidence exists for the window at all.</summary>
        None
    }

    /// <summary>
    /// The coverage boundary prepared for display.
    /// </summary>
    /// <param name="State">Complete, partial, or none.</param>
    /// <param name="CoverageFromUtc">Earliest instant evidence exists for, when there is one.</param>
    /// <param name="UncoveredDays">Whole days at the start of the window with no persisted evidence.</param>
    /// <param name="RequestedDays">Whole days the operator asked for.</param>
    public sealed record CoverageView(
        CoverageState State,
        DateTimeOffset? CoverageFromUtc,
        int UncoveredDays,
        int RequestedDays)
    {
        /// <summary>Whether a zero anywhere in this report can be read as a measured zero.</summary>
        public bool ZeroIsMeasured => State == CoverageState.Complete;
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
        duration.Key,
        DurationLabel(duration.Key, duration.Definition),
        duration.Definition,
        duration.SampleCount,
        FormatSeconds(duration.MinimumSeconds),
        FormatSeconds(duration.AverageSeconds),
        FormatSeconds(duration.MaximumSeconds));

    /// <summary>
    /// Reads the coverage boundary the report was built against.
    /// </summary>
    /// <param name="coverage">Server coverage block.</param>
    /// <returns>Coverage state and the size of the uncovered head of the window.</returns>
    /// <remarks>
    /// The arithmetic here converts two server timestamps into whole days for the notice. It measures
    /// nothing: the boundary, the window, and the completeness flag are all the server's, and the
    /// verdict is taken from <c>coverageComplete</c> rather than recomputed from the dates.
    /// </remarks>
    public static CoverageView Coverage(ReportingEvidenceCoverageResponse coverage)
    {
        int requestedDays = WholeDays(coverage.RequestedToUtc - coverage.RequestedFromUtc);

        if (coverage.CoverageComplete)
        {
            return new CoverageView(CoverageState.Complete, coverage.CoverageFromUtc, 0, requestedDays);
        }

        if (coverage.CoverageFromUtc is not { } from)
        {
            return new CoverageView(CoverageState.None, null, requestedDays, requestedDays);
        }

        // A boundary at or after the window's end means nothing in the window is covered.
        TimeSpan uncovered = from >= coverage.RequestedToUtc
            ? coverage.RequestedToUtc - coverage.RequestedFromUtc
            : from - coverage.RequestedFromUtc;

        return new CoverageView(
            CoverageState.Partial,
            from,
            Math.Max(0, WholeDays(uncovered)),
            requestedDays);
    }

    private static int WholeDays(TimeSpan span) =>
        span <= TimeSpan.Zero ? 0 : (int)Math.Round(span.TotalDays, MidpointRounding.AwayFromZero);

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
    /// Turkish label for a duration, chosen by its stable key.
    /// </summary>
    /// <param name="key">Stable server key, the only thing that identifies the interval.</param>
    /// <param name="definition">Server definition text, used as fallback display only.</param>
    /// <returns>Short Turkish label, or the server's own definition when the key is unknown.</returns>
    /// <remarks>
    /// Keyed on <paramref name="key"/> and never on <paramref name="definition"/> or array position.
    /// The contract is explicit that English text and ordering are presentation details: a reworded
    /// definition or a reordered array must not change what the UI thinks a row means. An unknown key
    /// falls through to the server's text, so an interval added later renders truthfully rather than
    /// being mislabelled as one of these three.
    /// </remarks>
    public static string DurationLabel(string key, string definition) => key switch
    {
        "importToPreview" => "İçe alma → önizleme",
        "claimToJiraCreation" => "Devralma → Jira kaydı",
        "claimToCompletion" => "Devralma → tamamlanma",
        _ => definition
    };

    /// <summary>
    /// Turkish rendering of one reported data limitation, chosen by its stable code.
    /// </summary>
    /// <param name="limitation">Limitation as the API returned it.</param>
    /// <returns>Reviewed Turkish text, the server's fallback message, or the bare code.</returns>
    /// <remarks>
    /// <para>
    /// Keyed on <c>code</c>, which the contract states is the identity; <c>message</c> is fallback
    /// presentation text and must never drive behaviour. Each translation below is a reviewed
    /// rendering of one specific limitation, not a paraphrase — a limitation that gets softened in
    /// translation stops doing its job, which is to stop a reader treating an unmeasured zero as a
    /// measured one.
    /// </para>
    /// <para>
    /// An unknown code degrades in the safe direction: the server's own message if it sent one,
    /// otherwise the code itself. A code shown raw on a Turkish screen is a blemish; a silently
    /// dropped limitation is a false statement about the data.
    /// </para>
    /// </remarks>
    public static string LimitationLabel(DataLimitationResponse limitation) => limitation.Code switch
    {
        "RateLimitRejectionsUnavailable"
            => "Hız sınırı reddi şu anda denetim kaydına yazılmıyor; bu ölçüm kullanılamıyor.",

        "AccessVersionConflictHistoryUnavailable"
            => "Geçmiş erişim sürüm çakışmaları, denetim kaydı bulunmadığında ölçülemez.",

        "OperationalSourceOutageHistoryUnavailable"
            => "Operasyonel kayıt kaynak sorgusu kesintileri, denetim kaydı bulunmadığında ölçülemez.",

        "BulkIdentityInvalidItemHistoryUnavailable"
            => "Geçmiş toplu kimlik isteklerinde atlanan geçersiz kayıtların ayrı sonuç denetim satırı yoktur.",

        "DuplicateCreatePreventionHistoryIncomplete"
            => "Mükerrer oluşturma engelleme, bu olayı denetim kaydına yazan ilk sürümden itibaren ölçülebilir.",

        "ElapsedDurationsNotActiveEffort"
            => "Geçen süreler bekleme ve yeniden denemeleri içerir; aktif çalışma süresi, kazanılan zaman "
               + "veya operatör performansı değildir.",

        "HistoryBeforePersistenceUnavailable"
            => "İstenen aralığın bir bölümü, kalıcı kayıt tutulmaya başlanmadan öncesine denk geliyor; "
               + "o bölüm için kanıt yok.",

        _ => string.IsNullOrWhiteSpace(limitation.Message) ? limitation.Code : limitation.Message
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
