using MudBlazor;
using SecureOps.Domain.ServiceAccounts;
using SecureOps.Shared.Contracts.ServiceAccounts;
using BadgeTone = SecureOps.Ui.Shared.Components.SoStatusBadge.BadgeTone;

namespace SecureOps.Ui.Services.ServiceAccounts;

/// <summary>
/// Wording, tone and icon of the "Hangi sunucularda?" view. The server decides every status; this only maps its answer to
/// words, a tone and an icon (colour is never the only signal). "Bulunamadı" is never worded as "kullanılmıyor".
/// </summary>
public static class WhereUsedPresentation
{
    /// <summary>Source filter choices (value sent to the API, label shown).</summary>
    public static IReadOnlyList<(string? Value, string Label)> SourceFilters { get; } =
    [
        (null, "Tüm kaynaklar"),
        (WhereUsedSources.Scan, "Tarama (web ve betik)"),
        (WhereUsedSources.Finding, "Excel bulgusu"),
        (WhereUsedSources.Manual, "Elle kayıt"),
        (WhereUsedSources.Spn, "SPN")
    ];

    /// <summary>Badge text of one evidence line: source, what it said and, when known, when.</summary>
    public static string EvidenceLabel(WhereUsedEvidence evidence) =>
        $"{WhereUsedLabels.Source(evidence.Source, evidence.Tool)} · {WhereUsedLabels.Outcome(evidence.Outcome)}{(evidence.At is { } at ? " · " + Day(at) : string.Empty)}";

    /// <summary>Local calendar day (Europe/Istanbul) of an instant.</summary>
    public static string Day(DateTimeOffset instant) => TimeZoneInfo.ConvertTime(instant, ReportCalendar.Istanbul).ToString("dd.MM.yyyy");

    /// <summary>Tone of an evidence line.</summary>
    public static BadgeTone EvidenceTone(string outcome) => outcome switch
    {
        WhereUsedOutcomes.Found => BadgeTone.Positive,
        WhereUsedOutcomes.NotFound => BadgeTone.Neutral,
        WhereUsedOutcomes.Defined => BadgeTone.Info,
        _ => BadgeTone.Caution
    };

    /// <summary>Icon of an evidence line.</summary>
    public static string EvidenceIcon(string outcome) => outcome switch
    {
        WhereUsedOutcomes.Found => Icons.Material.Filled.CheckCircle,
        WhereUsedOutcomes.NotFound => Icons.Material.Filled.RemoveCircleOutline,
        WhereUsedOutcomes.Defined => Icons.Material.Filled.Dns,
        _ => Icons.Material.Filled.HelpOutline
    };

    /// <summary>Tone of a row status.</summary>
    public static BadgeTone StatusTone(string status) => status switch
    {
        WhereUsedStatuses.Conflict => BadgeTone.Caution,
        WhereUsedStatuses.Found => BadgeTone.Positive,
        WhereUsedStatuses.DefinedOnly => BadgeTone.Info,
        _ => BadgeTone.Neutral
    };

    /// <summary>Icon of a row status.</summary>
    public static string StatusIcon(string status) => status switch
    {
        WhereUsedStatuses.Conflict => Icons.Material.Filled.CompareArrows,
        WhereUsedStatuses.Found => Icons.Material.Filled.CheckCircle,
        WhereUsedStatuses.DefinedOnly => Icons.Material.Filled.Dns,
        WhereUsedStatuses.NotFoundInScanned => Icons.Material.Filled.RemoveCircleOutline,
        _ => Icons.Material.Filled.HelpOutline
    };

    /// <summary>Short source name used in the source overview.</summary>
    public static string SourceName(string source) => source switch
    {
        WhereUsedSources.Scan => "Tarama",
        WhereUsedSources.Finding => "Excel bulgusu",
        WhereUsedSources.Manual => "Elle kayıt",
        WhereUsedSources.Spn => "SPN",
        _ => source
    };

    /// <summary>Label of a source state; "Bilinmiyor" and "Yetki yok" are not the same as "Kayıt yok".</summary>
    public static string StateLabel(string state) => state switch
    {
        WhereUsedSourceStates.Available => "Okundu",
        WhereUsedSourceStates.Empty => "Kayıt yok",
        WhereUsedSourceStates.Partial => "Kısmen okundu",
        WhereUsedSourceStates.NotInstalled => "Kurulu değil",
        WhereUsedSourceStates.Denied => "Yetki yok",
        WhereUsedSourceStates.NotConfigured => "Yapılandırılmamış",
        _ => "Bilinmiyor"
    };

    /// <summary>Tone of a source state: only a fully read source or a plain "nothing recorded" is calm.</summary>
    public static BadgeTone StateTone(string state) => state switch
    {
        WhereUsedSourceStates.Available => BadgeTone.Positive,
        WhereUsedSourceStates.Empty => BadgeTone.Neutral,
        _ => BadgeTone.Caution
    };

    /// <summary>True when a source did not answer completely, so the list may be missing servers.</summary>
    public static bool IsIncomplete(WhereUsedSourceStatus status) =>
        status.State is WhereUsedSourceStates.Partial or WhereUsedSourceStates.NotInstalled or WhereUsedSourceStates.Denied
            or WhereUsedSourceStates.NotConfigured or WhereUsedSourceStates.Unknown;

    /// <summary>The one-line summary above the list ("N sunucu, son tarama tarihi").</summary>
    public static string Headline(WhereUsedSummary summary)
    {
        string last = summary.LastScanAt is { } at ? $"son tarama {Day(at)}" : "taranmış kayıt yok";
        return $"{summary.Servers} sunucu · {summary.Found} sunucuda görüldü · {summary.Conflicts} çelişki · {last}";
    }

    /// <summary>The line shown under a server's name: last time a source saw it and last time anything looked.</summary>
    public static string Dates(WhereUsedServer server)
    {
        string seen = server.LastSeenAt is { } seenAt ? $"En son görülme {Day(seenAt)}" : "Hiçbir kaynakta görülmedi";
        string checkedAt = server.LastCheckedAt is { } at ? $" · son bakış {Day(at)}" : string.Empty;
        return $"{seen}{checkedAt} · {server.SourceCount} kaynak";
    }
}
