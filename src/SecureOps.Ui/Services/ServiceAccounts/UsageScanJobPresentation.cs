using MudBlazor;
using SecureOps.Shared.Contracts.ServiceAccounts;
using SecureOps.Ui.Shared.Components;

namespace SecureOps.Ui.Services.ServiceAccounts;

/// <summary>
/// How a web usage scan is shown (pure, from the server's view only): every tile has an icon and text besides its colour, "bulunamadı"
/// is always qualified, a server without an answer is "bilgi yok", and screen-reader announcements are kept to milestones.
/// </summary>
public static class UsageScanJobPresentation
{
    /// <summary>Poll interval while a scan is queued or running.</summary>
    public static TimeSpan PollInterval { get; set; } = TimeSpan.FromMilliseconds(1500);

    /// <summary>True while the scan is queued or running.</summary>
    public static bool IsActive(UsageScanJobView view) => view.State is "Queued" or "Running";

    /// <summary>CSS modifier of a tile (colour is a second signal; icon and text carry the meaning).</summary>
    public static string TileClass(string tile) => "sa-wscan-tile--" + tile.ToLowerInvariant();

    /// <summary>Icon of a tile.</summary>
    public static string TileIcon(string tile) => tile switch
    {
        "Pending" => Icons.Material.Outlined.HourglassEmpty,
        "Connecting" => Icons.Material.Filled.Sync,
        "Found" => Icons.Material.Filled.CheckCircle,
        "NotFound" => Icons.Material.Outlined.SearchOff,
        "Uncertain" => Icons.Material.Filled.HelpOutline,
        _ => Icons.Material.Filled.CloudOff
    };

    /// <summary>Badge tone of the job state.</summary>
    public static SoStatusBadge.BadgeTone StateTone(string state) => state switch
    {
        "Completed" => SoStatusBadge.BadgeTone.Positive,
        "Running" or "Queued" => SoStatusBadge.BadgeTone.Info,
        "Cancelled" => SoStatusBadge.BadgeTone.Caution,
        _ => SoStatusBadge.BadgeTone.Critical
    };

    /// <summary>Icon of the job state.</summary>
    public static string StateIcon(string state) => state switch
    {
        "Completed" => Icons.Material.Filled.TaskAlt,
        "Running" => Icons.Material.Filled.Sync,
        "Queued" => Icons.Material.Outlined.Schedule,
        "Cancelled" => Icons.Material.Filled.Block,
        _ => Icons.Material.Filled.ErrorOutline
    };

    /// <summary>Whole-number percentage of finished servers.</summary>
    public static int Percent(UsageScanJobCounts counts) => counts.Planned == 0 ? 0 : (int)Math.Floor(100.0 * counts.Done / counts.Planned);

    /// <summary>The progress line under the bar.</summary>
    public static string ProgressText(UsageScanJobView view)
    {
        UsageScanJobCounts c = view.Counts;
        string text = $"{c.Done} / {c.Planned} sunucu bitti";
        if (c.Connecting > 0)
        {
            text += $" · {c.Connecting} sunucu okunuyor";
        }

        if (view.State == "Queued")
        {
            return text + " · Worker'ın işi alması bekleniyor";
        }

        return view.State != "Running" ? text
            : view.EstimatedSecondsRemaining is { } seconds ? text + " · " + Remaining(seconds)
            : text + " · kalan süre en az 3 sunucu bitince tahmin edilir";
    }

    /// <summary>"yaklaşık 45 sn kaldı (tahmin)" / "yaklaşık 3 dk kaldı (tahmin)".</summary>
    public static string Remaining(int seconds) => seconds < 60
        ? $"yaklaşık {Math.Max(seconds, 1)} sn kaldı (tahmin)"
        : $"yaklaşık {(int)Math.Ceiling(seconds / 60.0)} dk kaldı (tahmin)";

    /// <summary>
    /// What the polite live region says after a poll, or null when nothing worth saying changed. Milestones only: the first view, a
    /// state change (with the summary at the end) and newly found usages, never every tile.
    /// </summary>
    public static string? Announcement(UsageScanJobView? previous, UsageScanJobView current, int newItems)
    {
        if (previous is null)
        {
            return $"Tarama {current.StateLabel.ToLowerInvariant()}: {current.Counts.Done} / {current.Counts.Planned} sunucu bitti.";
        }

        if (previous.State != current.State)
        {
            return current.Summary is { } summary ? $"Tarama {current.StateLabel.ToLowerInvariant()}. {summary}" : $"Tarama {current.StateLabel.ToLowerInvariant()}.";
        }

        return newItems switch
        {
            0 => null,
            1 => "1 yeni kullanım bulundu.",
            _ => $"{newItems} yeni kullanım bulundu."
        };
    }

    /// <summary>Per-server status line for the tile's accessible text.</summary>
    public static string TileAccessibleText(UsageScanJobServerView server) =>
        server.ExpectedMatches > 0 ? $"{server.ServerName}: {server.TileText}; gMSA ile çalışan {server.ExpectedMatches} bileşen" : $"{server.ServerName}: {server.TileText}";
}
