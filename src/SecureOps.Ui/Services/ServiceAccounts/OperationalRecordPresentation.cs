using SecureOps.Shared.Contracts.ServiceAccounts;
using SecureOps.Ui.Shared.Components;

namespace SecureOps.Ui.Services.ServiceAccounts;

/// <summary>
/// Display rules for the plan's OR panel (ADR-0030, PROPOSED; OR-2). Labels come from the server; this only picks tone and icon
/// (never colour alone: every badge carries its text) and says "bilinmiyor" wherever the ITSM did not prove a value.
/// </summary>
public static class OperationalRecordPresentation
{
    /// <summary>Text for a value the ITSM did not prove.</summary>
    public const string UnknownText = "bilinmiyor";

    /// <summary>The value, or "bilinmiyor" when it is null or blank (never "yok", never zero).</summary>
    public static string Known(string? value) => string.IsNullOrWhiteSpace(value) ? UnknownText : value;

    /// <summary>A count only when its part is proven; otherwise "bilinmiyor" (a proven zero stays 0).</summary>
    public static string Count(bool known, int? count) => known && count is { } value ? value.ToString(System.Globalization.CultureInfo.InvariantCulture) : UnknownText;

    /// <summary>Tone per state: proven records positive, everything a person must look at caution, nothing sent neutral.</summary>
    public static SoStatusBadge.BadgeTone StateTone(string state) => state switch
    {
        "Created" or "Linked" => SoStatusBadge.BadgeTone.Positive,
        "Sending" or "OutcomeUnknown" or "PartiallyCreated" => SoStatusBadge.BadgeTone.Caution,
        _ => SoStatusBadge.BadgeTone.Neutral
    };

    /// <summary>Icon per state.</summary>
    public static string StateIcon(string state) => state switch
    {
        "Created" or "Linked" => MudBlazor.Icons.Material.Filled.CheckCircleOutline,
        "Sending" or "OutcomeUnknown" => MudBlazor.Icons.Material.Filled.HelpOutline,
        "PartiallyCreated" => MudBlazor.Icons.Material.Filled.WarningAmber,
        _ => MudBlazor.Icons.Material.Filled.RemoveCircleOutline
    };

    /// <summary>How a linked number was checked; null for states without a link.</summary>
    public static string? VerificationLabel(string? verification) => verification switch
    {
        "Found" => "Turuncu Hat aramasında bulundu",
        "NotVerified" => "Doğrulanamadı (arama bulamadı veya cevap vermedi; bu \"yok\" demek değildir)",
        _ => null
    };

    /// <summary>Provider badge: Fake is always called synthetic; Disabled says the feature is off.</summary>
    public static (string Label, SoStatusBadge.BadgeTone Tone) ProviderBadge(PlanOperationalRecordsView view) => view.Provider switch
    {
        "Disabled" => ("OR açma kapalı", SoStatusBadge.BadgeTone.Neutral),
        _ when view.Synthetic => ("Sentetik sağlayıcı (Fake)", SoStatusBadge.BadgeTone.Caution),
        _ => (view.Provider, SoStatusBadge.BadgeTone.Neutral)
    };

    /// <summary>Why OR opening is unavailable for the whole plan or one account.</summary>
    public static string BlockedText(string? reason) => reason switch
    {
        "creationDisabled" => "OR açma bu ortamda kapalı (sunucu yapılandırması varsayılanı). Kayıtlı OR'lar ve durumları yine görünür.",
        "planNotApproved" => "OR yalnız onaylanmış veya uygulamadaki planda açılır.",
        "planClosed" => "Plan kapandı veya iptal edildi; yeni OR açılmaz. Açılmış OR Turuncu Hat'ta açık kalır; gerekiyorsa orada kapatın.",
        "noPermission" => "Bu plan için OR açma yetkiniz veya kapsamınız görünmüyor (Çalışma yetkisi ve planın tüm hesaplarında sorumlu dayanak). Sunucu her işlemde yeniden karar verir.",
        "submissionExists" => "Bu hesap için açılmış veya sonucu bekleyen bir OR var; ikinci OR açılmaz.",
        _ => "OR şu an açılamıyor."
    };

    /// <summary>The plan reference line a person searches for in Turuncu Hat (the description's last line), or null.</summary>
    public static string? PlanReference(OperationalRecordSubmissionView submission) =>
        submission.Description.Split('\n') is { Length: > 0 } lines && lines[^1].StartsWith("WASAS plan ", StringComparison.Ordinal) ? lines[^1] : null;

    /// <summary>Requester badge: only Matched with a location may send.</summary>
    public static (string Label, SoStatusBadge.BadgeTone Tone) RequesterBadge(OperationalRecordRequesterView requester) =>
        requester.Status == "Matched" && requester.HasHomeLocation
            ? ("Eşleşti", SoStatusBadge.BadgeTone.Positive)
            : ("Eşleşmedi — OR açılamaz", SoStatusBadge.BadgeTone.Critical);
}
