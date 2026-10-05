using SecureOps.Domain.ServiceAccounts;

namespace SecureOps.Ui.Services.ServiceAccounts;

/// <summary>Display helpers: Turkish labels from the domain vocabulary and explicit unknown-date wording.</summary>
public static class ServiceAccountUiText
{
    /// <summary>Action types with Turkish labels, in workflow order.</summary>
    public static IReadOnlyList<(string Value, string Label)> Actions { get; } =
        [.. Enum.GetValues<ServiceAccountActionType>().Select(a => (a.ToString(), ServiceAccountLabels.Action(a)))];

    /// <summary>Communication kinds with Turkish labels.</summary>
    public static IReadOnlyList<(string Value, string Label)> CommunicationKinds { get; } =
        [.. Enum.GetValues<CommunicationKind>().Select(k => (k.ToString(), ServiceAccountLabels.CommunicationKindLabel(k)))];

    /// <summary>Date or an explicit "unknown" statement (unknown is never shown as a date).</summary>
    public static string Date(DateOnly? date, string unknown = "Tarih bilinmiyor") => date?.ToString("dd.MM.yyyy") ?? unknown;

    /// <summary>Local timestamp shown with its zone.</summary>
    public static string Instant(DateTimeOffset? instant) =>
        instant is { } value ? TimeZoneInfo.ConvertTime(value, ReportCalendar.Istanbul).ToString("dd.MM.yyyy HH:mm") + " (TR)" : "Saat bilinmiyor";

    /// <summary>Picker value → date.</summary>
    public static DateOnly? ToDate(DateTime? value) => value is { } date ? DateOnly.FromDateTime(date) : null;

    /// <summary>Date → picker value.</summary>
    public static DateTime? ToPicker(DateOnly? value) => value?.ToDateTime(TimeOnly.MinValue);

    /// <summary>Action type label from its stored code.</summary>
    public static string Action(string code) => Enum.TryParse(code, out ServiceAccountActionType type) ? ServiceAccountLabels.Action(type) : code;

    /// <summary>Communication direction label.</summary>
    public static string Direction(string code) => code == "Incoming" ? "Gelen" : "Giden";

    /// <summary>Communication kind label.</summary>
    public static string CommunicationKind(string code) =>
        Enum.TryParse(code, out CommunicationKind kind) ? ServiceAccountLabels.CommunicationKindLabel(kind) : code;

    /// <summary>Ownership state label.</summary>
    public static string Ownership(string state) => state switch
    {
        "Confirmed" => "Teyitli",
        "Proposed" => "Öneri (teyit bekliyor)",
        "Rejected" => "Reddedildi",
        "Ended" => "Sona erdi",
        _ => state
    };

    /// <summary>Handover status label.</summary>
    public static string Handover(string status) =>
        Enum.TryParse(status, out HandoverStatus value) ? ServiceAccountLabels.Handover(value) : status;

    /// <summary>gMSA suitability label.</summary>
    public static string Suitability(string value) =>
        Enum.TryParse(value, out GmsaSuitability suitability) ? ServiceAccountLabels.Suitability(suitability) : value;

    /// <summary>Finding status label.</summary>
    public static string Finding(string status) => Enum.TryParse(status, out FindingStatus value) ? ServiceAccountLabels.Finding(value) : status;

    /// <summary>Close outcome label.</summary>
    public static string Outcome(string? outcome) => outcome switch
    {
        "Completed" => "Tamamlandı",
        "NotNeeded" => "Gerek kalmadı",
        "Cancelled" => "İptal edildi",
        _ => "Açık"
    };

    /// <summary>Longest gMSA name Active Directory accepts, without the trailing <c>$</c> (the shared domain rule).</summary>
    public const int GmsaNameLimit = SecureOps.Domain.ServiceAccounts.ServiceAccountGmsaName.Limit;

    /// <summary>
    /// Advice while typing an account name; never a block (the server and Active Directory decide). The name is counted
    /// without a <c>DOMAIN\</c> prefix, a UPN suffix or the trailing <c>$</c>. Above 15 characters a <c>$</c> name cannot be
    /// a gMSA as typed; any other name is valid as a user account, so the hint only asks for a shorter name for a planned
    /// gMSA conversion. Null when there is nothing to say.
    /// </summary>
    public static string? GmsaNameHint(string? accountName)
    {
        bool gmsa = SecureOps.Domain.ServiceAccounts.ServiceAccountGmsaName.HasGmsaSuffix(accountName);
        string bare = SecureOps.Domain.ServiceAccounts.ServiceAccountGmsaName.Bare(accountName);
        if (bare.Length <= GmsaNameLimit)
        {
            return null;
        }

        return gmsa
            ? $"gMSA adı {bare.Length} karakter (sondaki $ hariç); Active Directory gMSA adlarını en çok {GmsaNameLimit} karakterle sınırlar, bu ad gMSA olarak oluşturulamayabilir. Kayıt engellenmez; adı sunucu ve Active Directory doğrular."
            : $"Ad {bare.Length} karakter. Bu hesap gMSA'ya dönüştürülecekse yeni gMSA adı (sondaki $ hariç) en çok {GmsaNameLimit} karakter olabilir; daha kısa bir gMSA adı planlayın. Kayıt engellenmez.";
    }

    /// <summary>
    /// Advice while typing a requested gMSA name (work request or transition), counted by the same rule. Above the limit the
    /// server refuses the name; the hint says so before sending but never disables the button (the server decides).
    /// </summary>
    public static string? RequestedGmsaNameHint(string? requestedName)
    {
        int length = SecureOps.Domain.ServiceAccounts.ServiceAccountGmsaName.Length(requestedName);
        return length <= GmsaNameLimit ? null
            : $"İstenen gMSA adı {length} karakter (domain öneki, UPN eki ve sondaki $ hariç). Active Directory en çok {GmsaNameLimit} karakter kabul eder; bu ad kısaltılır veya oluşturulamaz. Sunucu bu adı kaydetmez, daha kısa bir ad yazın.";
    }

    /// <summary>Helper text under a requested gMSA name field: the counted length against the limit.</summary>
    public static string RequestedGmsaNameHelper(string? requestedName) =>
        $"{SecureOps.Domain.ServiceAccounts.ServiceAccountGmsaName.Length(requestedName)}/{GmsaNameLimit} karakter (domain öneki, UPN eki ve sondaki $ sayılmaz)";

    /// <summary>Reference text (OR/OCO/Jira).</summary>
    public static string References(IReadOnlyList<SecureOps.Shared.Contracts.ServiceAccounts.SaExternalRef> references) =>
        references.Count == 0 ? "Referans yok" : string.Join(", ", references.Select(r => r.Type + " " + r.Number));
}
