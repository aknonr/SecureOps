using System.Text;
using SecureOps.Shared.Contracts.ServiceAccounts;

namespace SecureOps.Ui.Services.ServiceAccounts;

/// <summary>What changed for one planned server between two scans of the same account and purpose.</summary>
public enum SaServerChange
{
    /// <summary>Same outcome (including "still no information").</summary>
    Unchanged,
    /// <summary>Not found before, found now.</summary>
    NewlyFound,
    /// <summary>Found before, not found now in a complete scan (never "no longer used").</summary>
    NoLongerFound,
    /// <summary>No information before, a usable result now.</summary>
    InformationArrived,
    /// <summary>A usable result before, none now; the earlier answer is not replaced by "not used".</summary>
    InformationLost,
    /// <summary>Not in the earlier plan.</summary>
    NewlyPlanned,
    /// <summary>In the earlier plan, absent from this one.</summary>
    NotPlannedNow
}

/// <summary>What changed for one matched component.</summary>
public enum SaComponentChange
{
    /// <summary>Matched now, not before.</summary>
    Added,
    /// <summary>Matched before; the server was fully scanned now and it was not found.</summary>
    NotFoundNow,
    /// <summary>Matched before; the server was not fully scanned now, so nothing is known.</summary>
    UnknownNow,
    /// <summary>Same component, other configured identity.</summary>
    IdentityChanged
}

/// <summary>One server row of a scan comparison.</summary>
public sealed record SaServerDiff(string Server, SaServerChange Change, string Text);

/// <summary>One component row of a scan comparison.</summary>
public sealed record SaComponentDiff(string Server, string Component, SaComponentChange Change, string Text);

/// <summary>Comparison of a scan with the previous scan of the same purpose.</summary>
public sealed record SaScanDiff(UsageScanView Current, UsageScanView Previous, IReadOnlyList<SaServerDiff> Servers,
    IReadOnlyList<SaComponentDiff> Components, string? GmsaText)
{
    /// <summary>Servers whose result changed.</summary>
    public IReadOnlyList<SaServerDiff> ChangedServers => [.. Servers.Where(s => s.Change != SaServerChange.Unchanged)];

    /// <summary>Servers with the same outcome.</summary>
    public int UnchangedServers => Servers.Count(s => s.Change == SaServerChange.Unchanged);

    /// <summary>Whether anything differs.</summary>
    public bool HasChanges => ChangedServers.Count > 0 || Components.Count > 0 || GmsaText is not null;
}

/// <summary>
/// Compares two usage scans from the loaded detail (UI only; no new data). "Not found" only follows a fully scanned server
/// and never means "not used"; a server or component that is missing from the newer scan because that scan did not cover it
/// is "unknown", not "gone" (SPEC rule 15).
/// </summary>
public static class ServiceAccountScanDiff
{
    /// <summary>The scan to compare with: the next older scan of the same purpose in a newest-first list.</summary>
    public static UsageScanView? PreviousOf(IReadOnlyList<UsageScanView> newestFirst, int index) =>
        newestFirst.Skip(index + 1).FirstOrDefault(p => p.Purpose == newestFirst[index].Purpose);

    /// <summary>Compares <paramref name="current"/> with <paramref name="previous"/>.</summary>
    public static SaScanDiff Compute(UsageScanView current, UsageScanView previous)
    {
        Dictionary<string, UsageScanServerView> before = Index(previous.Servers);
        Dictionary<string, UsageScanServerView> now = Index(current.Servers);

        List<SaServerDiff> servers = [];
        foreach (string name in now.Keys.Union(before.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
        {
            before.TryGetValue(name, out UsageScanServerView? p);
            now.TryGetValue(name, out UsageScanServerView? c);
            string display = c?.ServerName ?? p!.ServerName;
            servers.Add(Server(display, p, c));
        }

        List<SaComponentDiff> components = [];
        Dictionary<string, UsageScanItemView> oldItems = Items(previous);
        Dictionary<string, UsageScanItemView> newItems = Items(current);
        foreach ((string key, UsageScanItemView item) in newItems.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
        {
            string label = $"{item.ComponentTypeLabel}: {item.ComponentName}";
            if (!oldItems.TryGetValue(key, out UsageScanItemView? old))
            {
                bool wasCovered = before.TryGetValue(item.ServerName, out UsageScanServerView? p) && Covered(p);
                components.Add(new(item.ServerName, label, SaComponentChange.Added, wasCovered
                    ? "Bu taramada yeni görüldü."
                    : "Bu taramada görüldü; önceki taramada bu sunucu tam taranmamıştı, bileşen yeni olmayabilir."));
            }
            else if (!string.Equals(old.ConfiguredIdentity, item.ConfiguredIdentity, StringComparison.OrdinalIgnoreCase))
            {
                components.Add(new(item.ServerName, label, SaComponentChange.IdentityChanged, "Yapılandırılmış kimlik değişmiş görünüyor; yeni kimliği kendiniz doğrulayın."));
            }
        }

        foreach ((string key, UsageScanItemView old) in oldItems.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (newItems.ContainsKey(key))
            {
                continue;
            }

            string label = $"{old.ComponentTypeLabel}: {old.ComponentName}";
            components.Add(now.TryGetValue(old.ServerName, out UsageScanServerView? c) && Covered(c)
                ? new(old.ServerName, label, SaComponentChange.NotFoundNow, "Bu taramada, sunucu tam tarandığı hâlde bulunmadı. Bu, bileşenin kullanılmadığını göstermez.")
                : new(old.ServerName, label, SaComponentChange.UnknownNow, "Bu taramada sunucu tam taranmadı; bileşenin durumu bilinmiyor."));
        }

        string? gmsa = current.Gmsa is { } now2 && previous.Gmsa is { } before2 && now2.Conclusion != before2.Conclusion
            ? $"gMSA kanıtı: önceki tarama \"{before2.ConclusionLabel}\", bu tarama \"{now2.ConclusionLabel}\". Bu tarama doğrulama değildir."
            : null;
        return new SaScanDiff(current, previous, servers, components, gmsa);
    }

    /// <summary>Planned servers a person should scan again: no usable result (incomplete, unreachable, failed) in this scan.</summary>
    public static IReadOnlyList<string> RescanServers(UsageScanView scan) =>
        [.. scan.Servers.Where(s => !Covered(s)).Select(s => s.ServerName).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase)];

    /// <summary>Planned-server list in the collector's input format (one name per line, # comments).</summary>
    public static string RescanFile(UsageScanView scan)
    {
        IReadOnlyList<string> names = RescanServers(scan);
        StringBuilder text = new();
        text.Append("# Yeniden taranacak sunucular (önceki taramada tam sonuç gelmeyenler)\r\n");
        text.Append("# Kaynak tarama dosyası: ").Append(scan.FileName).Append("\r\n");
        foreach (string name in names)
        {
            text.Append(name).Append("\r\n");
        }

        return text.ToString();
    }

    private static bool Covered(UsageScanServerView server) => server.Outcome is "Found" or "NotFound";

    private static Dictionary<string, UsageScanServerView> Index(IEnumerable<UsageScanServerView> servers)
    {
        Dictionary<string, UsageScanServerView> map = new(StringComparer.OrdinalIgnoreCase);
        foreach (UsageScanServerView server in servers)
        {
            map[server.ServerName] = server;
        }

        return map;
    }

    private static Dictionary<string, UsageScanItemView> Items(UsageScanView scan)
    {
        Dictionary<string, UsageScanItemView> map = new(StringComparer.OrdinalIgnoreCase);
        foreach (UsageScanItemView item in scan.Items.Where(i => i.Role == "Former"))
        {
            map[$"{item.ServerName}|{item.ComponentType}|{item.ComponentName}"] = item;
        }

        return map;
    }

    private static SaServerDiff Server(string name, UsageScanServerView? p, UsageScanServerView? c)
    {
        if (p is null)
        {
            return new(name, SaServerChange.NewlyPlanned, $"Önceki planda yoktu; bu taramada: {c!.OutcomeLabel}.");
        }

        if (c is null)
        {
            return new(name, SaServerChange.NotPlannedNow, "Bu taramanın planında yok; bu sunucu için güncel bilgi yok.");
        }

        return (Covered(p), Covered(c)) switch
        {
            (true, true) when p.Outcome == "Found" && c.Outcome == "NotFound" =>
                new(name, SaServerChange.NoLongerFound, "Önceden çalışıyordu, bu taramada tam taranan kaynaklarda bulunmadı. Bu, kullanılmadığını göstermez."),
            (true, true) when p.Outcome == "NotFound" && c.Outcome == "Found" => new(name, SaServerChange.NewlyFound, "Önceden bulunmamıştı, bu taramada çalışıyor."),
            (true, true) => new(name, SaServerChange.Unchanged, c.OutcomeLabel),
            (false, true) => new(name, SaServerChange.InformationArrived, $"Önceden bilgi yoktu; bu taramada: {c.OutcomeLabel}."),
            (true, false) => new(name, SaServerChange.InformationLost, $"Önceki tarama: {p.OutcomeLabel}; bu taramada sonuç eksik ({c.ResultLabel}). Önceki cevap \"kullanılmıyor\" anlamına gelmez, güncel bilgi yok."),
            _ => new(name, SaServerChange.Unchanged, $"Hâlâ bilgi yok ({c.ResultLabel}).")
        };
    }
}
