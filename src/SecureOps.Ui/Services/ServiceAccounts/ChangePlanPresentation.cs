using System.Text.Json;
using SecureOps.Shared.Contracts.ServiceAccounts;
using SecureOps.Ui.Shared.Components;

namespace SecureOps.Ui.Services.ServiceAccounts;

/// <summary>State of one step of the plan flow (text and icon always accompany the colour).</summary>
public enum PlanStepState
{
    /// <summary>Finished: the server recorded it.</summary>
    Done,

    /// <summary>The step the plan is waiting on.</summary>
    Current,

    /// <summary>Not reachable yet.</summary>
    Upcoming,

    /// <summary>The plan was cancelled before this step.</summary>
    Stopped
}

/// <summary>One step of the Plan → Önizleme → Onay → Kontrol listesi → Doğrulama flow.</summary>
/// <param name="Key">Stable key (plan, preview, approval, checklist, verification).</param>
/// <param name="Title">Short step name.</param>
/// <param name="State">Where the plan stands on this step.</param>
/// <param name="Missing">What is missing or what happens next, in plain words.</param>
public sealed record PlanStep(string Key, string Title, PlanStepState State, string Missing);

/// <summary>
/// Wording and decisions the plan pages share. Everything here is a pure function of the server's <see cref="ChangePlanView"/>:
/// the UI reflects what the server returned and never decides authorization or invents an outcome.
/// </summary>
public static class ChangePlanPresentation
{
    /// <summary>Plan flag codes in display order (the order the approver reads the counters).</summary>
    public static readonly IReadOnlyList<string> Flags =
        ["Ok", "StaleScan", "NoScan", "NotCovered", "ManualOnly", "NothingFound", "NameTooLong"];

    /// <summary>Statuses offered by the list filter, as (code, label).</summary>
    public static readonly IReadOnlyList<(string Code, string Label)> StatusFilters =
    [
        ("", "Tümü"), ("Draft", "Taslak"), ("Previewed", "Önizlendi, onay bekliyor"), ("Approved", "Onaylandı"),
        ("InProgress", "Uygulanıyor"), ("Completed", "Kapatıldı"), ("Cancelled", "İptal edildi")
    ];

    /// <summary>Tone of a plan status badge.</summary>
    public static SoStatusBadge.BadgeTone StatusTone(string status) => status switch
    {
        "Draft" => SoStatusBadge.BadgeTone.Neutral,
        "Previewed" => SoStatusBadge.BadgeTone.Caution,
        "Approved" or "InProgress" => SoStatusBadge.BadgeTone.Info,
        "Completed" => SoStatusBadge.BadgeTone.Positive,
        _ => SoStatusBadge.BadgeTone.Neutral
    };

    /// <summary>Icon of a plan status badge.</summary>
    public static string StatusIcon(string status) => status switch
    {
        "Draft" => MudBlazor.Icons.Material.Filled.EditNote,
        "Previewed" => MudBlazor.Icons.Material.Filled.HourglassTop,
        "Approved" => MudBlazor.Icons.Material.Filled.TaskAlt,
        "InProgress" => MudBlazor.Icons.Material.Filled.Checklist,
        "Completed" => MudBlazor.Icons.Material.Filled.Done,
        _ => MudBlazor.Icons.Material.Filled.Block
    };

    /// <summary>Short counter label of a flag; the long server label stays on each row. Never says "unused".</summary>
    public static string FlagShortLabel(string flag) => flag switch
    {
        "Ok" => "Taramada bulundu",
        "StaleScan" => "Eski bilgi",
        "NoScan" => "Bilgi yok",
        "NotCovered" => "Eksik olabilir",
        "ManualOnly" => "Elle yapılacak",
        "NothingFound" => "Taramada bulunamadı",
        "NameTooLong" => "Ad çok uzun",
        _ => flag
    };

    /// <summary>Tone of a flag badge. No flag blocks the plan, so none is Critical except a name that cannot be used.</summary>
    public static SoStatusBadge.BadgeTone FlagTone(string flag) => flag switch
    {
        "Ok" => SoStatusBadge.BadgeTone.Positive,
        "StaleScan" or "NotCovered" => SoStatusBadge.BadgeTone.Caution,
        "NameTooLong" => SoStatusBadge.BadgeTone.Critical,
        "ManualOnly" or "NothingFound" => SoStatusBadge.BadgeTone.Info,
        _ => SoStatusBadge.BadgeTone.Neutral
    };

    /// <summary>Icon of a flag badge (a second signal beside the text).</summary>
    public static string FlagIcon(string flag) => flag switch
    {
        "Ok" => MudBlazor.Icons.Material.Filled.CheckCircleOutline,
        "StaleScan" => MudBlazor.Icons.Material.Filled.Schedule,
        "NoScan" => MudBlazor.Icons.Material.Filled.HelpOutline,
        "NotCovered" => MudBlazor.Icons.Material.Filled.WarningAmber,
        "ManualOnly" => MudBlazor.Icons.Material.Filled.PanTool,
        "NothingFound" => MudBlazor.Icons.Material.Filled.SearchOff,
        "NameTooLong" => MudBlazor.Icons.Material.Filled.ErrorOutline,
        _ => MudBlazor.Icons.Material.Filled.Info
    };

    /// <summary>Count of one flag from the preview summary.</summary>
    public static int Count(ChangePlanFlagCounts counts, string flag) => flag switch
    {
        "Ok" => counts.Ok,
        "StaleScan" => counts.StaleScan,
        "NoScan" => counts.NoScan,
        "NotCovered" => counts.NotCovered,
        "ManualOnly" => counts.ManualOnly,
        "NothingFound" => counts.NothingFound,
        "NameTooLong" => counts.NameTooLong,
        _ => 0
    };

    /// <summary>
    /// What a flag means for the person who reads it (approver and implementer). "Not found" is never "not used".
    /// </summary>
    public static string FlagExplanation(string flag) => flag switch
    {
        "Ok" => "Hesap, son keşif taramasında bu sunucuda bu bileşende bulundu.",
        "StaleScan" => "Bulgu 7 günden eski bir taramadan geliyor; bugünkü durum farklı olabilir. Plan engellenmez.",
        "NoScan" => "Hesap için yüklenmiş keşif taraması yok; bu hesapta bilgi yok. Kullanılmadığı anlamına gelmez.",
        "NotCovered" => "Sunucu taranamadı veya kısmen tarandı; bileşen listesi eksik olabilir.",
        "ManualOnly" => "IIS site/uygulama \"connect as\" kimliği gMSA ile desteklenmiyor veya belirsiz; elle ele alınır.",
        "NothingFound" => "Son tarama cevap verdi ama hesabı taranan hiçbir sunucuda bulamadı. \"Kullanılmıyor\" demek değildir; yalnız bulunamadı.",
        "NameTooLong" => "Hedef gMSA adı 15 karakter sınırını aşıyor.",
        _ => string.Empty
    };

    /// <summary>The five steps and where the plan stands, derived only from what the server returned.</summary>
    public static IReadOnlyList<PlanStep> Steps(ChangePlanView plan)
    {
        bool cancelled = plan.Status == "Cancelled";
        bool previewed = plan.Preview is not null;
        bool approved = plan.Approval is not null;
        bool completed = plan.Status == "Completed";
        bool reachedChecklist = plan.Status is "Approved" or "InProgress" or "Completed";

        PlanStepState Open(bool done, bool current) => done ? PlanStepState.Done : cancelled ? PlanStepState.Stopped : current ? PlanStepState.Current : PlanStepState.Upcoming;

        return
        [
            new("plan", "Plan", PlanStepState.Done,
                plan.Status == "Draft" ? "Hesap listesi kayıtlı ve değiştirilebilir; sonra önizleme isteyin." : "Hesap listesi kayıtlı."),
            new("preview", "Önizleme", Open(previewed && plan.Status != "Draft", plan.Status == "Draft" && plan.Accounts.Count > 0),
                previewed ? $"Önizleme sürüm {plan.Preview!.Version} hazır." : "Her hesabın son keşif taramasından bileşen satırları üretilir; henüz önizleme yok."),
            new("approval", "Onay", Open(approved, plan.Status == "Previewed"),
                approved ? "Onaylandı." : plan.Status == "Previewed" ? "Planı hazırlamayan bir doğrulama yetkilisi bu önizlemeyi onaylamalı." : "Önizleme olmadan onay verilemez."),
            new("checklist", "Kontrol listesi", completed ? PlanStepState.Done : reachedChecklist ? PlanStepState.Current : cancelled ? PlanStepState.Stopped : PlanStepState.Upcoming,
                completed ? "Plan kapatıldı; işaretler ve kanıtlar saklanır."
                    : reachedChecklist ? ChecklistProgress(plan.Checklist)
                    : "Onaydan sonra açılır. Sistem sunucuda hiçbir şey değiştirmez."),
            new("verification", "Doğrulama", completed ? PlanStepState.Current : cancelled ? PlanStepState.Stopped : PlanStepState.Upcoming,
                completed ? "Kapanış doğrulama sayılmaz: gMSA kontrol taraması ve hesap sayfasındaki doğrulama ayrıca yapılır."
                    : "Değişiklikten sonra gMSA kontrol taraması yüklenir ve doğrulama yetkilisi hesabı doğrular.")
        ];
    }

    /// <summary>Short Turkish label of a step state (read by screen readers and shown beside the colour).</summary>
    public static string StepStateLabel(PlanStepState state) => state switch
    {
        PlanStepState.Done => "Tamamlandı",
        PlanStepState.Current => "Sırada",
        PlanStepState.Upcoming => "Sonra",
        _ => "Durduruldu"
    };

    /// <summary>The check states a person can record, in display order, as (code, label).</summary>
    public static readonly IReadOnlyList<(string Code, string Label)> CheckStates =
        [("Done", "Yapıldı"), ("Skipped", "Atlandı"), ("Failed", "Başarısız"), ("RolledBack", "Geri alındı")];

    /// <summary>Tone of a check badge ("Done" is the person's statement, not a verification).</summary>
    public static SoStatusBadge.BadgeTone CheckTone(string state) => state switch
    {
        "Done" => SoStatusBadge.BadgeTone.Positive,
        "Failed" => SoStatusBadge.BadgeTone.Critical,
        "RolledBack" => SoStatusBadge.BadgeTone.Caution,
        _ => SoStatusBadge.BadgeTone.Neutral
    };

    /// <summary>Icon of a check badge (a second signal beside the text).</summary>
    public static string CheckIcon(string state) => state switch
    {
        "Done" => MudBlazor.Icons.Material.Filled.TaskAlt,
        "Skipped" => MudBlazor.Icons.Material.Filled.SkipNext,
        "Failed" => MudBlazor.Icons.Material.Filled.ErrorOutline,
        "RolledBack" => MudBlazor.Icons.Material.Filled.Undo,
        _ => MudBlazor.Icons.Material.Filled.RadioButtonUnchecked
    };

    /// <summary>States offered for a row whose latest mark is <paramref name="latest"/>: "RolledBack" only after Done or Failed (the server decides again).</summary>
    public static IReadOnlyList<(string Code, string Label)> OfferedStates(string? latest) =>
        [.. CheckStates.Where(s => s.Code != "RolledBack" || latest is "Done" or "Failed")];

    /// <summary>A note is required for every state except Done (a hint for the form; the server decides).</summary>
    public static bool NoteRequired(string state) => state != "Done";

    /// <summary>Progress sentence of the checklist from the server's counters.</summary>
    public static string ChecklistProgress(ChangePlanChecklistView? checklist) => checklist switch
    {
        null => "Bileşenleri kendi yetkinizle, kendi değişiklik kaydınızla sunucuda yaparsınız; yaptığınızı satır satır işaretleyin. İşaret sizin beyanınızdır.",
        { Total: 0 } => "İşaretlenecek satır yok.",
        { Unchecked: 0 } => $"{checklist.Total} satırın hepsi işaretli; plan kapatılabilir.",
        _ => $"{checklist.Total} satırdan {checklist.Total - checklist.Unchecked} tanesi işaretli, {checklist.Unchecked} tanesi bekliyor."
    };

    /// <summary>Counters shown above the rows; zero counters are kept so an absence is visible.</summary>
    public static IReadOnlyList<(string Label, int Count, SoStatusBadge.BadgeTone Tone, string Icon)> ChecklistCounters(ChangePlanChecklistView c) =>
    [
        ("Yapıldı (beyan)", c.Done, CheckTone("Done"), CheckIcon("Done")),
        ("Atlandı", c.Skipped, CheckTone("Skipped"), CheckIcon("Skipped")),
        ("Başarısız", c.Failed, CheckTone("Failed"), CheckIcon("Failed")),
        ("Geri alındı", c.RolledBack, CheckTone("RolledBack"), CheckIcon("RolledBack")),
        ("İşaretlenmedi", c.Unchecked, SoStatusBadge.BadgeTone.Neutral, CheckIcon(string.Empty))
    ];

    /// <summary>Label of a plan event (operational record, not a performance view).</summary>
    public static string EventLabel(string name) => name switch
    {
        "Created" => "Plan oluşturuldu",
        "Updated" => "Hesap listesi değişti",
        "Previewed" => "Önizleme üretildi",
        "Approved" => "Önizleme onaylandı",
        "Started" => "İlk işaret kaydedildi (uygulama başladı)",
        "Completed" => "Plan kapatıldı",
        "Cancelled" => "Plan iptal edildi",
        _ => name
    };

    /// <summary>Label for the per-account answer to a create or update request.</summary>
    public static SoStatusBadge.BadgeTone OutcomeTone(string outcome) =>
        outcome == "Accepted" ? SoStatusBadge.BadgeTone.Positive : SoStatusBadge.BadgeTone.Critical;

    /// <summary>First 12 characters of the digest, enough to recognise the preview the approver looked at.</summary>
    public static string ShortDigest(string sha) => sha.Length <= 12 ? sha : sha[..12];

    /// <summary>One time format on every Service Accounts screen: Istanbul time with "(TR)" (same as <see cref="ServiceAccountUiText.Instant"/>).</summary>
    public static string Instant(DateTimeOffset value) => ServiceAccountUiText.Instant(value);

    /// <summary>
    /// The 409 body carries the plan view the caller may already read; null when the response has none or it does not parse
    /// (the page then reloads from the server instead).
    /// </summary>
    public static ChangePlanView? CurrentPlan(string? currentJson) => Parse<ChangePlanView>(currentJson);

    /// <summary>The per-account results of a 400 <c>ServiceAccountChangePlanAccountsRefused</c>.</summary>
    public static IReadOnlyList<ChangePlanAccountResult> AccountResults(string? currentJson) =>
        Parse<List<ChangePlanAccountResult>>(currentJson) ?? [];

    private static T? Parse<T>(string? json) where T : class
    {
        if (json is not { Length: > 0 })
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(json, ApiResponseReader.JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
