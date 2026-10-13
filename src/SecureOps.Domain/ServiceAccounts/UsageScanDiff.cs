namespace SecureOps.Domain.ServiceAccounts;

/// <summary>What changed for one planned server between two scans of the same account and purpose.</summary>
public enum ScanServerChange
{
    /// <summary>Same outcome (including "still no information").</summary>
    Unchanged,
    /// <summary>Not found before in a complete scan, found now.</summary>
    NewlyFound,
    /// <summary>Found before, not found now in a complete scan (never "no longer used").</summary>
    NoLongerFound,
    /// <summary>No usable result before, a usable result now.</summary>
    InformationArrived,
    /// <summary>A usable result before, none now; the earlier answer is not replaced by "not used".</summary>
    InformationLost,
    /// <summary>Not in the earlier plan.</summary>
    NewlyPlanned,
    /// <summary>In the earlier plan, absent from this one.</summary>
    NotPlannedNow
}

/// <summary>What changed for one matched former-account component.</summary>
public enum ScanComponentChange
{
    /// <summary>Matched now, not before.</summary>
    Added,
    /// <summary>Matched before; the server was fully scanned now and the component was not found.</summary>
    NotFoundNow,
    /// <summary>Matched before; the server was not fully scanned now (partial, failed, unreachable or not planned), so nothing is known.</summary>
    UnknownNow,
    /// <summary>Same component, other configured identity.</summary>
    IdentityChanged
}

/// <summary>One planned server of a scan with the number of components matched to the account's searched name.</summary>
public sealed record ScanDiffServer(string ServerName, ScanServerResult Result, int FormerMatches);

/// <summary>One component matched to the account's searched name (role Former).</summary>
public sealed record ScanDiffItem(string ServerName, string ComponentType, string ComponentName, string ConfiguredIdentity);

/// <summary>Comparison row for one server.</summary>
public sealed record ScanServerDiff(string ServerName, ScanAccountOutcome? Previous, ScanAccountOutcome? Current, ScanServerChange Change, string Text);

/// <summary>Comparison row for one component.</summary>
public sealed record ScanComponentDiff(string ServerName, string ComponentType, string ComponentName, ScanComponentChange Change, string Text,
    string? PreviousIdentity, string? CurrentIdentity);

/// <summary>Complete comparison of a scan with an older one; servers by name, components by change, server and name.</summary>
public sealed record ScanDiffResult(IReadOnlyList<ScanServerDiff> Servers, IReadOnlyList<ScanComponentDiff> Components);

/// <summary>
/// Compares two usage scans of one account over every matched component (G-33). Rules (SPEC rule 15): "not found" exists only
/// for a fully scanned server and never means "not used"; a server or component the newer scan did not cover is "unknown",
/// never "gone"; an unchanged result is not a usage verdict. Server names and component keys compare case-insensitively.
/// </summary>
public static class UsageScanDiff
{
    /// <summary>Compares <paramref name="currentServers"/>/<paramref name="currentItems"/> with the older scan.</summary>
    public static ScanDiffResult Compute(IEnumerable<ScanDiffServer> previousServers, IEnumerable<ScanDiffItem> previousItems,
        IEnumerable<ScanDiffServer> currentServers, IEnumerable<ScanDiffItem> currentItems)
    {
        Dictionary<string, ScanDiffServer> before = ByName(previousServers);
        Dictionary<string, ScanDiffServer> now = ByName(currentServers);

        List<ScanServerDiff> servers = [];
        foreach (string name in now.Keys.Union(before.Keys, StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase))
        {
            ScanAccountOutcome? p = before.TryGetValue(name, out ScanDiffServer? ps) ? UsageScanOutcomes.Outcome(ps.Result, ps.FormerMatches) : null;
            ScanAccountOutcome? c = now.TryGetValue(name, out ScanDiffServer? cs) ? UsageScanOutcomes.Outcome(cs.Result, cs.FormerMatches) : null;
            servers.Add(Server(cs?.ServerName ?? ps!.ServerName, p, c, cs?.Result));
        }

        Dictionary<string, ScanDiffItem> oldItems = ByKey(previousItems);
        Dictionary<string, ScanDiffItem> newItems = ByKey(currentItems);
        List<ScanComponentDiff> components = [];
        foreach ((string key, ScanDiffItem item) in newItems)
        {
            if (!oldItems.TryGetValue(key, out ScanDiffItem? old))
            {
                bool wasCovered = before.TryGetValue(item.ServerName, out ScanDiffServer? ps) && ps.Result == ScanServerResult.Success;
                components.Add(new(item.ServerName, item.ComponentType, item.ComponentName, ScanComponentChange.Added, wasCovered
                    ? "Bu taramada yeni görüldü."
                    : "Bu taramada görüldü; önceki taramada bu sunucu tam taranmamıştı, bileşen yeni olmayabilir.", null, item.ConfiguredIdentity));
            }
            else if (!string.Equals(old.ConfiguredIdentity, item.ConfiguredIdentity, StringComparison.OrdinalIgnoreCase))
            {
                components.Add(new(item.ServerName, item.ComponentType, item.ComponentName, ScanComponentChange.IdentityChanged,
                    "Yapılandırılmış kimlik değişmiş görünüyor; yeni kimliği kendiniz doğrulayın.", old.ConfiguredIdentity, item.ConfiguredIdentity));
            }
        }

        foreach ((string key, ScanDiffItem old) in oldItems)
        {
            if (newItems.ContainsKey(key))
            {
                continue;
            }

            components.Add(now.TryGetValue(old.ServerName, out ScanDiffServer? cs) && cs.Result == ScanServerResult.Success
                ? new(old.ServerName, old.ComponentType, old.ComponentName, ScanComponentChange.NotFoundNow,
                    "Bu taramada, sunucu tam tarandığı hâlde bulunmadı. Bu, bileşenin kullanılmadığını göstermez.", old.ConfiguredIdentity, null)
                : new(old.ServerName, old.ComponentType, old.ComponentName, ScanComponentChange.UnknownNow,
                    "Bu taramada sunucu tam taranmadı; bileşenin durumu bilinmiyor.", old.ConfiguredIdentity, null));
        }

        return new ScanDiffResult(servers, [.. components.OrderBy(c => c.Change).ThenBy(c => c.ServerName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.ComponentType, StringComparer.Ordinal).ThenBy(c => c.ComponentName, StringComparer.OrdinalIgnoreCase)]);
    }

    /// <summary>Turkish label of a server change.</summary>
    public static string ServerChangeLabel(ScanServerChange value) => value switch
    {
        ScanServerChange.NewlyFound => "Yeni bulundu",
        ScanServerChange.NoLongerFound => "Bu taramada bulunmadı",
        ScanServerChange.InformationArrived => "Bilgi geldi",
        ScanServerChange.InformationLost => "Güncel bilgi yok",
        ScanServerChange.NewlyPlanned => "Plana eklendi",
        ScanServerChange.NotPlannedNow => "Planda yok",
        _ => "Aynı"
    };

    /// <summary>Turkish label of a component change.</summary>
    public static string ComponentChangeLabel(ScanComponentChange value) => value switch
    {
        ScanComponentChange.Added => "Yeni görüldü",
        ScanComponentChange.NotFoundNow => "Bu taramada bulunmadı",
        ScanComponentChange.UnknownNow => "Durumu bilinmiyor",
        _ => "Kimlik değişmiş"
    };

    private static Dictionary<string, ScanDiffServer> ByName(IEnumerable<ScanDiffServer> servers)
    {
        Dictionary<string, ScanDiffServer> map = new(StringComparer.OrdinalIgnoreCase);
        foreach (ScanDiffServer server in servers)
        {
            map[server.ServerName] = server;
        }

        return map;
    }

    private static Dictionary<string, ScanDiffItem> ByKey(IEnumerable<ScanDiffItem> items)
    {
        Dictionary<string, ScanDiffItem> map = new(StringComparer.OrdinalIgnoreCase);
        foreach (ScanDiffItem item in items)
        {
            map[$"{item.ServerName}|{item.ComponentType}|{item.ComponentName}"] = item;
        }

        return map;
    }

    private static ScanServerDiff Server(string name, ScanAccountOutcome? p, ScanAccountOutcome? c, ScanServerResult? currentResult)
    {
        if (p is null)
        {
            return new(name, null, c, ScanServerChange.NewlyPlanned, $"Önceki planda yoktu; bu taramada: {UsageScanOutcomes.OutcomeLabel(c!.Value)}.");
        }

        if (c is null)
        {
            return new(name, p, null, ScanServerChange.NotPlannedNow, "Bu taramanın planında yok; bu sunucu için güncel bilgi yok.");
        }

        bool before = p is ScanAccountOutcome.Found or ScanAccountOutcome.NotFound;
        bool now = c is ScanAccountOutcome.Found or ScanAccountOutcome.NotFound;
        string result = UsageScanOutcomes.ResultLabel(currentResult!.Value);
        return (before, now) switch
        {
            (true, true) when p == ScanAccountOutcome.Found && c == ScanAccountOutcome.NotFound => new(name, p, c, ScanServerChange.NoLongerFound,
                "Önceden çalışıyordu, bu taramada tam taranan kaynaklarda bulunmadı. Bu, kullanılmadığını göstermez."),
            (true, true) when p == ScanAccountOutcome.NotFound && c == ScanAccountOutcome.Found => new(name, p, c, ScanServerChange.NewlyFound,
                "Önceden bulunmamıştı, bu taramada çalışıyor."),
            (true, true) => new(name, p, c, ScanServerChange.Unchanged, UsageScanOutcomes.OutcomeLabel(c.Value)),
            (false, true) => new(name, p, c, ScanServerChange.InformationArrived, $"Önceden bilgi yoktu; bu taramada: {UsageScanOutcomes.OutcomeLabel(c.Value)}."),
            (true, false) => new(name, p, c, ScanServerChange.InformationLost,
                $"Önceki tarama: {UsageScanOutcomes.OutcomeLabel(p.Value)}; bu taramada sonuç eksik ({result}). Önceki cevap \"kullanılmıyor\" anlamına gelmez, güncel bilgi yok."),
            _ => new(name, p, c, ScanServerChange.Unchanged, $"Hâlâ bilgi yok ({result}).")
        };
    }
}
