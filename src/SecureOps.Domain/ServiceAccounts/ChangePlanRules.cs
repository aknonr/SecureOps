using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace SecureOps.Domain.ServiceAccounts;

/// <summary>Change plan status (CHANGE-PLAN-DESIGN.md). Open = every status except Completed and Cancelled.</summary>
public enum ChangePlanStatus
{
    /// <summary>Being prepared; no valid preview.</summary>
    Draft,
    /// <summary>A preview version is current and may be approved.</summary>
    Previewed,
    /// <summary>The current preview is approved; the people may work through the checklist.</summary>
    Approved,
    /// <summary>At least one checklist mark was recorded (PR 2).</summary>
    InProgress,
    /// <summary>Closed after the work; never means the accounts are verified.</summary>
    Completed,
    /// <summary>Cancelled with a reason; records are kept.</summary>
    Cancelled
}

/// <summary>What a preview row tells the approver. No flag blocks a plan.</summary>
public enum ChangePlanFlag
{
    /// <summary>Found by a scan newer than <see cref="ChangePlanRules.ScanFreshDays"/> days on an answered server.</summary>
    Ok,
    /// <summary>The scan is older than <see cref="ChangePlanRules.ScanFreshDays"/> days ("eski bilgi").</summary>
    StaleScan,
    /// <summary>The account has no Discovery scan: one "no information" row (never "not used").</summary>
    NoScan,
    /// <summary>The server was not scanned or only partly: the component list may be incomplete.</summary>
    NotCovered,
    /// <summary>IIS site/application/virtual directory "connect as": not supported with a gMSA or unclear; done by hand.</summary>
    ManualOnly,
    /// <summary>The target name is longer than Active Directory accepts (031 rule).</summary>
    NameTooLong
}

/// <summary>One account to preview: its target gMSA name and its latest Discovery scan (null when it has none).</summary>
public sealed record ChangePlanPreviewAccount(Guid AccountId, string TargetGmsaName, ChangePlanScanSource? Scan);

/// <summary>The account's latest Discovery scan as linked to it: link, scan time, planned servers and matched components.</summary>
public sealed record ChangePlanScanSource(Guid LinkId, DateTimeOffset ScanAt, IReadOnlyList<ChangePlanScanServer> Servers,
    IReadOnlyList<ChangePlanScanComponent> Components);

/// <summary>A planned server of a scan and its result (Success, Partial, Failed, Unreachable, NoResult).</summary>
public sealed record ChangePlanScanServer(string ServerName, string Result);

/// <summary>A component the scan found running as the account.</summary>
public sealed record ChangePlanScanComponent(string ServerName, string ComponentType, string ComponentName, string CurrentIdentity);

/// <summary>One preview row; also the unit the preview digest is computed from.</summary>
public sealed record ChangePlanPreviewRow(Guid AccountId, Guid? ScanLinkId, string? ServerName, string? ComponentType, string? ComponentName,
    string? CurrentIdentity, string TargetIdentity, ChangePlanFlag Flag, DateTimeOffset? ScanAt);

/// <summary>Pure rules of the gMSA conversion change plan: bounds, preview rows, flags, digest and approver separation.</summary>
public static class ChangePlanRules
{
    /// <summary>Most accounts in one plan.</summary>
    public const int MaxAccounts = 20;

    /// <summary>A scan older than this many days is "eski bilgi" (owner decision 3); the plan is not blocked.</summary>
    public const int ScanFreshDays = 7;

    /// <summary>Longest plan title.</summary>
    public const int MaxTitle = 200;

    /// <summary>Longest reason (approval, cancel).</summary>
    public const int MaxReason = 1000;

    /// <summary>Longest OCO number (same bound as an external record reference).</summary>
    public const int MaxOcoNumber = 64;

    private static readonly string[] _manualOnly = ["IisSite", "IisApplication", "IisVirtualDirectory"];

    /// <summary>Whether the plan still holds its accounts (owner decision 6: an account is in at most one open plan).</summary>
    public static bool IsOpen(ChangePlanStatus status) => status is not (ChangePlanStatus.Completed or ChangePlanStatus.Cancelled);

    /// <summary>Normalized OCO number, or null when it does not have the external-reference format (owner decision 4: format only).</summary>
    public static string? OcoNumber(string? value) =>
        ServiceAccountText.RecordNumber(value) is { Length: <= MaxOcoNumber } number ? number : null;

    /// <summary>
    /// Approver separation (design T3): the approver is not the planner (<c>approverIsPlanner</c>) and is not the person who
    /// produced the approved preview or anyone who changed the plan's account list after it was created
    /// (<c>approverChangedPlan</c>). Null when the approver may approve.
    /// </summary>
    public static string? ApproverRefusal(Guid approver, Guid planner, Guid previewer, IEnumerable<Guid> editors) =>
        approver == planner ? "approverIsPlanner"
        : approver == previewer || editors.Contains(approver) ? "approverChangedPlan"
        : null;

    /// <summary>
    /// Preview rows from each account's latest Discovery scan, in canonical order. An account without a scan gets one NoScan
    /// row; a server the scan did not fully answer flags its components NotCovered, or gets one NotCovered row when nothing
    /// was found there. A server that answered and found nothing has no row: absence from a scan is not a component.
    /// </summary>
    public static IReadOnlyList<ChangePlanPreviewRow> BuildPreview(IEnumerable<ChangePlanPreviewAccount> accounts, DateTimeOffset now)
    {
        List<ChangePlanPreviewRow> rows = [];
        foreach (ChangePlanPreviewAccount account in accounts)
        {
            bool nameTooLong = ServiceAccountGmsaName.ExceedsLimit(account.TargetGmsaName);
            if (account.Scan is not { } scan)
            {
                rows.Add(new ChangePlanPreviewRow(account.AccountId, null, null, null, null, null, account.TargetGmsaName, ChangePlanFlag.NoScan, null));
                continue;
            }

            bool stale = now - scan.ScanAt > TimeSpan.FromDays(ScanFreshDays);
            HashSet<string> uncovered = new(scan.Servers.Where(s => s.Result != "Success").Select(s => s.ServerName), StringComparer.Ordinal);
            foreach (ChangePlanScanComponent component in scan.Components)
            {
                ChangePlanFlag flag = _manualOnly.Contains(component.ComponentType, StringComparer.Ordinal) ? ChangePlanFlag.ManualOnly
                    : uncovered.Contains(component.ServerName) ? ChangePlanFlag.NotCovered
                    : stale ? ChangePlanFlag.StaleScan
                    : nameTooLong ? ChangePlanFlag.NameTooLong
                    : ChangePlanFlag.Ok;
                rows.Add(new ChangePlanPreviewRow(account.AccountId, scan.LinkId, component.ServerName, component.ComponentType, component.ComponentName,
                    component.CurrentIdentity, account.TargetGmsaName, flag, scan.ScanAt));
            }

            foreach (string server in uncovered.Where(s => !scan.Components.Any(c => c.ServerName == s)))
            {
                rows.Add(new ChangePlanPreviewRow(account.AccountId, scan.LinkId, server, null, null, null, account.TargetGmsaName, ChangePlanFlag.NotCovered, scan.ScanAt));
            }
        }

        return Canonical(rows);
    }

    /// <summary>
    /// SHA-256 (lower-case hex) of the rows in canonical order: account, server, type and name (ordinal), ties broken by the
    /// whole row; every field length-prefixed so field boundaries are unambiguous; times as UTC ticks. Computed only on the
    /// server, from stored rows, so a client can neither choose it nor reorder its way to another value.
    /// </summary>
    public static string Digest(IEnumerable<ChangePlanPreviewRow> rows)
    {
        StringBuilder text = new();
        foreach (ChangePlanPreviewRow row in Canonical(rows))
        {
            text.Append(Line(row)).Append('\n');
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString()))).ToLowerInvariant();
    }

    /// <summary>Turkish label of a flag; never claims a component is unused.</summary>
    public static string FlagLabel(ChangePlanFlag flag) => flag switch
    {
        ChangePlanFlag.Ok => "Taramada bulundu",
        ChangePlanFlag.StaleScan => $"Eski bilgi: tarama {ScanFreshDays} günden eski",
        ChangePlanFlag.NoScan => "Bilgi yok: hesapta keşif taraması yok (kullanılmıyor anlamına gelmez)",
        ChangePlanFlag.NotCovered => "Sunucu taranamadı veya kısmen tarandı: liste eksik olabilir",
        ChangePlanFlag.ManualOnly => "Elle: IIS \"connect as\" kimliği gMSA ile desteklenmiyor veya belirsiz",
        _ => $"gMSA adı {ServiceAccountGmsaName.Limit} karakterden uzun"
    };

    /// <summary>Turkish label of a status.</summary>
    public static string StatusLabel(ChangePlanStatus status) => status switch
    {
        ChangePlanStatus.Draft => "Taslak",
        ChangePlanStatus.Previewed => "Önizlendi, onay bekliyor",
        ChangePlanStatus.Approved => "Onaylandı",
        ChangePlanStatus.InProgress => "Uygulanıyor",
        ChangePlanStatus.Completed => "Kapatıldı",
        _ => "İptal edildi"
    };

    private static List<ChangePlanPreviewRow> Canonical(IEnumerable<ChangePlanPreviewRow> rows) =>
        [.. rows.Select(r => (Row: r, Line: Line(r)))
            .OrderBy(x => x.Row.AccountId.ToString("D"), StringComparer.Ordinal)
            .ThenBy(x => x.Row.ServerName ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(x => x.Row.ComponentType ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(x => x.Row.ComponentName ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(x => x.Line, StringComparer.Ordinal)
            .Select(x => x.Row)];

    private static string Line(ChangePlanPreviewRow row) => string.Concat(
        Field(row.AccountId.ToString("D")), Field(row.ScanLinkId?.ToString("D")), Field(row.ServerName), Field(row.ComponentType),
        Field(row.ComponentName), Field(row.CurrentIdentity), Field(row.TargetIdentity), Field(row.Flag.ToString()),
        Field(row.ScanAt?.UtcTicks.ToString(CultureInfo.InvariantCulture)));

    private static string Field(string? value) => value is null ? "~|" : value.Length.ToString(CultureInfo.InvariantCulture) + ":" + value + "|";
}
