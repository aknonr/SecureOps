using SecureOps.Shared.Contracts.InUse;

namespace SecureOps.Ui.Services;

/// <summary>Tone of a one-line In Use status badge. Red (critical) is deliberately absent: a list row never proves a failure.</summary>
public enum InUseBadgeTone
{
    /// <summary>Nothing to do or not applicable.</summary>
    Neutral,

    /// <summary>Informational: work remains but nothing is wrong.</summary>
    Info,

    /// <summary>Needs the operator's attention before anything else.</summary>
    Caution,

    /// <summary>Local work is saved; says nothing about the source system.</summary>
    Positive
}

/// <summary>A status word with its tone. The text always carries the meaning; the tone is only reinforcement.</summary>
public sealed record InUseBadge(string Label, InUseBadgeTone Tone);

/// <summary>What the product knows about a finished record, one separate fact per field. Unknown is never rendered as "none".</summary>
/// <param name="Kind">Short status word.</param>
/// <param name="Tone">Badge tone.</param>
/// <param name="Meaning">One sentence on what the status does and does not prove.</param>
/// <param name="When">Time line with its source named, or "unknown".</param>
/// <param name="Reviewer">Who saved the answers, or unknown.</param>
/// <param name="Approver">Who approved the WASAS activity; the list does not carry it.</param>
/// <param name="Report">Which report version the completion intent used, or unknown.</param>
/// <param name="Intent">Last recorded completion intent stage in words, or the statement that none is recorded.</param>
public sealed record InUseClosedFacts(string Kind, InUseBadgeTone Tone, string Meaning, string When, string Reviewer,
    string Approver, string Report, string Intent);

/// <summary>
/// Wording for the In Use list rows and the closed/completed page. It only reads fields the server returned and never turns a
/// missing field into "done" or "none" (ADR-0020: acknowledgement, manual verification, WASAS activity completion and OR closure are
/// separate kinds of evidence).
/// </summary>
public static class InUsePresentation
{
    /// <summary>Servers whose three operator answers are all Yes or No in the saved draft.</summary>
    public static int SavedCompleted(InUseRecord record) => record.Source.Servers.Count(s => InUseChecks.OperatorCodes.All(c =>
        record.Draft?.Answers.Any(a => a.ServerId == s.Id && a.Check == c && a.Value is "Yes" or "No") == true));

    /// <summary>The one status badge of a list row: the most urgent true fact, in words.</summary>
    public static InUseBadge RowBadge(InUseRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        int total = record.Source.Servers.Count;
        return record.Discarded ? new("Taslak kaldırıldı", InUseBadgeTone.Neutral)
            : record.HasActiveExecution ? new("İşlem sonucu doğrulanmalı", InUseBadgeTone.Caution)
            : record.SourceObservationMissing ? new("Son yenilemede görülmedi", InUseBadgeTone.Caution)
            : record.Status == "Stale" ? new("Yeniden inceleme gerekli", InUseBadgeTone.Caution)
            : total == 0 ? new("Sunucu ilişkisi yok", InUseBadgeTone.Caution)
            : SavedCompleted(record) < total ? new("Cevaplar eksik", InUseBadgeTone.Info)
            : InUseProgress.Ready(record) ? new("İnceleme kaydedildi", InUseBadgeTone.Positive)
            : new("Kayıt kontrol edilmeli", InUseBadgeTone.Info);
    }

    /// <summary>The single next step of a list row.</summary>
    public static string NextStep(InUseRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return record.TrackingOnly ? "Kaynakta doğrulanan sonucu inceleyin; yeni WASAS onayı göndermeyin"
            : record.HasActiveExecution ? "Önceki işlemin sonucunu doğrulayın; yeni onay göndermeyin"
            : record.SourceObservationMissing ? "Kaynak durumunu doğrulayın; cevaplar korunuyor"
            : record.Status == "Stale" ? "Değişen kaynak bilgisini inceleyin; cevaplar korunuyor"
            : record.Source.Servers.Count == 0 ? "Sunucu ilişki kanıtı gerekli"
            : SavedCompleted(record) < record.Source.Servers.Count ? "Eksik cevapları tamamlayın"
            : "Excel önizlemesini inceleyin";
    }

    /// <summary>Whether the row carries something the operator must read before opening it (drives the quiet "!" cue, never red).</summary>
    public static bool NeedsAttention(InUseRecord record) => RowBadge(record).Tone == InUseBadgeTone.Caution;

    /// <summary>Wording of the service-item relationship state.</summary>
    public static string Relationship(string state, int? count) => state switch
    {
        "Complete" => count == 0 ? "İlişkili kayıt yok (doğrulandı)" : $"{count} doğrulanmış kayıt",
        "Observed" => $"{count} gözlenen kayıt; tamlık doğrulanmadı",
        "Partial" => $"Eksik sonuç; {count} kayıt korunuyor, güncel üyelik doğrulanmalı",
        "Forbidden" => "Kaynak ilişki erişimi yetersiz; önceki kanıt varsa korunur",
        "Failed" => "İlişki okuması başarısız; önceki kanıt varsa korunur",
        "Ambiguous" => "Eşleme belirsiz; önceki kanıt varsa korunur",
        _ => "Henüz sorgulanmadı / sözleşme bekleniyor"
    };

    /// <summary>Wording of the last recorded completion intent; none of these says the OR is closed unless the source verified it.</summary>
    public static string CompletionStage(string stage, string? finalOrState) => stage switch
    {
        "Blocked" => "Tamamlama sözleşme nedeniyle engelli; ek yüklenmedi, görev tamamlanmadı",
        "UploadPending" => "Ek yükleme sonucu bekleniyor",
        "UploadFailed" => "Ek yükleme başarısız; görev tamamlanmadı",
        "TaskLookupPending" => "Ek yüklendi; görev eşleşmesi bekleniyor",
        "TaskLookupBlocked" => "Ek yüklendi; görev eşleşmesi yok veya belirsiz",
        "CompletionPending" => "Görev tamamlama sonucu bekleniyor",
        "CompletionFailed" => "Ek yüklendi; görev tamamlama başarısız",
        "VerificationPending" => "Görev tamamlandı; OR son durumu kaynaktan doğrulanmadı",
        "TaskCompletedOrOpen" => $"Görev tamamlandı; OR durumu: {finalOrState ?? "bilinmiyor"}; kapalı olarak doğrulanmadı",
        "ClosedVerified" => "Görev tamamlandı; OR kapalı durumu kaynak okumasıyla doğrulandı",
        _ => "Sonuç belirsiz; mutabakat gerekli"
    };

    /// <summary>The separate facts the product holds about a record the server lists as completed or closed.</summary>
    public static InUseClosedFacts Closed(InUseRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        bool orClosed = InUseActivity.Has(record.Source.Lifecycle, "Closed");
        bool sourceActivity = InUseActivity.Has(record.Source.WasasActivity, "Completed");
        (string kind, InUseBadgeTone tone, string meaning) = sourceActivity
            ? ("WASAS aktivitesi tamamlandı", InUseBadgeTone.Positive, "Kaynak okuması aktivitenin tamamlandığını gösteriyor." + (orClosed ? " Genel OR da kapalı." : " Genel OR durumu ayrıca doğrulanmadı."))
            : record.ActivityVerifiedAt.HasValue
                ? ("WASAS aktivitesi onaylandı", InUseBadgeTone.Positive, "Onay yanıtı ürün kaydında doğrulandı; kaynaktan OR son durumu okunmadı." + (orClosed ? " Genel OR kaynakta kapalı görünüyor." : ""))
                : orClosed
                    ? ("Genel OR kapalı", InUseBadgeTone.Info, "Kaynak genel OR'u kapalı gösteriyor; WASAS aktivitesinin kendi durumu doğrulanmadı.")
                    : ("Durum doğrulanmadı", InUseBadgeTone.Neutral, "Sunucu bu kaydı tamamlanmış olarak listeledi ama nedenini bildirmedi.");
        string when = record.ActivityVerifiedAt is { } verified ? $"Aktivite onayı: {Time(verified)}"
            : record.Completion is { } intent ? $"Tamamlama niyeti: {Time(intent.ConfirmedAt)}"
            : "Zaman bu listede yok; kayıt ayrıntısına bakın";
        string reviewer = record.Draft is { } draft && (draft.ReviewedByLabel is not null || draft.ReviewedByAccount is not null)
            ? "İnceleyen (cevapları kaydeden): " + InUsePersonLabel.Format(draft.ReviewedByLabel, draft.ReviewedByAccount)
            : "İnceleyen: bilinmiyor";
        return new(kind, tone, meaning, when, reviewer,
            "Onaylayan: bu listede yok (kayıt ayrıntısında, Başlatan)",
            record.Completion is { } c ? $"Rapor sürümü {c.ReportVersion}" : "Rapor: bu listede yok; kayıt ayrıntısında arşivler görünür",
            record.Completion is { } last ? CompletionStage(last.Stage, last.FinalOrState) : "Bu kayıtta tamamlama niyeti kaydı yok");
    }

    /// <summary>A server field for display; absent is "Bilinmiyor", never an empty cell.</summary>
    public static string Field(InUseServer server, string field) => server.Fields.GetValueOrDefault(field)?.Value is { } value
        ? InUseDisplayText.Field(field, value) : "Bilinmiyor";

    /// <summary>What the source says about the WASAS activity, by the server's classification.</summary>
    public static string SourceActivity(InUseRecord record) => record.ActivityStatus switch
    {
        "Completed" => "Tamamlandığı kaynakta doğrulandı",
        "Pending" => "Kaynakta bekliyor",
        "OrClosed" => "Genel OR kapalı; WASAS aktivitesine ait doğrulama alınmadı",
        _ => "Doğrulama bekliyor; güncel WASAS durumu doğrulanmadı"
    };

    /// <summary>What the source says about the whole OR.</summary>
    public static string SourceLifecycle(InUseRecord record) => string.IsNullOrWhiteSpace(record.Source.Lifecycle?.Source) ? "Kaynak durumu alınmadı" : record.Source.Lifecycle?.Value switch
    { "Open" => "Açık", "Closed" => "Kapalı (kaynak kanıtı)", _ => "Kaynak durumu alınmadı" };

    /// <summary>Local date and time, one format everywhere.</summary>
    public static string Time(DateTimeOffset value) => value.ToLocalTime().ToString("dd.MM.yyyy HH:mm");
}

/// <summary>One figure of the In Use status strip. <paramref name="Total"/> is the server's own total for that view; null means the read failed.</summary>
/// <param name="Label">What is counted.</param>
/// <param name="Total">Server total, or null when it could not be read (shown as "alınamadı", never as zero).</param>
/// <param name="Href">The list that shows these records.</param>
/// <param name="Current">True when the list currently shows this view.</param>
/// <param name="Hint">One line on what the figure includes.</param>
public sealed record InUseCount(string Label, int? Total, string Href, bool Current, string Hint);
