using SecureOps.Domain.ServiceAccounts;
using SecureOps.Shared.Contracts.ServiceAccounts;

namespace SecureOps.Infrastructure.ServiceAccounts.WhereUsed;

/// <summary>What the directory said about the account's SPNs; <c>State</c> is one of <see cref="WhereUsedSourceStates"/>.</summary>
public sealed record WhereUsedSpnInput(string State, string Text, IReadOnlyList<SpnHost> Hosts)
{
    /// <summary>No SPN information (not asked or not available).</summary>
    public static WhereUsedSpnInput Unknown(string state, string text) => new(state, text, []);
}

/// <summary>
/// Merges the stored evidence sources and the optional SPN hosts into one row per server. Sources never overwrite each other:
/// every statement stays as its own evidence line, and the row only summarises them. Nothing here ever says "not used"; a
/// complete read that did not find the account is "not found in the scanned sources". A new source (DNA report, gMSA
/// attributes, inventory) adds another evidence producer next to the four below; the row logic does not change.
/// </summary>
public static class WhereUsedBuilder
{
    /// <summary>One statement of one source about one server name as written by that source.</summary>
    private sealed record Raw(ServerNameKey Name, WhereUsedEvidence Evidence);

    /// <summary>Builds every server row (unfiltered, ordered), the summary and the source statuses.</summary>
    public static (IReadOnlyList<WhereUsedServer> Servers, WhereUsedSummary Summary, IReadOnlyList<WhereUsedSourceStatus> Sources) Build(WhereUsedData data,
        WhereUsedSpnInput spn)
    {
        List<Raw> raw = [];
        raw.AddRange(ScanEvidence(data));
        raw.AddRange(FindingEvidence(data));
        raw.AddRange(ManualEvidence(data));
        raw.AddRange(SpnEvidence(spn));

        IReadOnlyDictionary<string, string> merged = WhereUsedServerNames.Merge(raw.Select(r => r.Name));
        WhereUsedServer[] servers = [.. raw.GroupBy(r => merged[r.Name.Key], StringComparer.Ordinal).Select(Row)
            .OrderBy(s => Rank(s.Status)).ThenByDescending(s => s.LastSeenAt ?? DateTimeOffset.MinValue).ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(s => s.Name, StringComparer.Ordinal)];

        DateTimeOffset? lastScan = data.Scans.Count == 0 ? null : data.Scans.Max(s => s.LastScannedAt ?? s.CombinedAt);
        WhereUsedSummary summary = new(servers.Length, servers.Count(s => s.Found), servers.Count(s => s.Conflict),
            servers.Count(s => s.Status == WhereUsedStatuses.DefinedOnly), servers.Count(s => s.Status == WhereUsedStatuses.NotFoundInScanned),
            servers.Count(s => s.Status == WhereUsedStatuses.NoInformation), lastScan, data.Scans.Count, Math.Max(0, data.ScansTotal - data.Scans.Count),
            data.UsagesWithoutServer, data.FindingsWithoutServer, data.ItemsTruncated);
        return (servers, summary, SourceStatuses(data, spn));
    }

    /// <summary>Keeps rows that have evidence from <paramref name="source"/> and, optionally, only rows a source found.</summary>
    public static IEnumerable<WhereUsedServer> Filter(IEnumerable<WhereUsedServer> servers, string? source, bool foundOnly) =>
        servers.Where(s => (source is null || s.Evidence.Any(e => e.Source == source)) && (!foundOnly || s.Found));

    /// <summary>Whether a source name is one of the known ones.</summary>
    public static bool IsSource(string? source) => source is WhereUsedSources.Scan or WhereUsedSources.Finding or WhereUsedSources.Manual or WhereUsedSources.Spn;

    /// <summary>Label of a scan tool: Combined = a person-run script uploaded, Jea = started from the web.</summary>
    public static string ToolLabel(string tool) => WhereUsedLabels.Tool(tool);

    /// <summary>Turkish label of a row status.</summary>
    public static string StatusLabel(string status) => status switch
    {
        WhereUsedStatuses.Conflict => "Kaynaklar çelişiyor",
        WhereUsedStatuses.Found => "Kullanımda görüldü",
        WhereUsedStatuses.DefinedOnly => "Yalnız SPN tanımlı",
        WhereUsedStatuses.NotFoundInScanned => "Taranan kaynaklarda bulunmadı (kullanılmıyor demek değildir)",
        _ => "Bilgi yok"
    };

    /// <summary>Short Turkish name of a source (a scan carries its tool).</summary>
    public static string SourceLabel(string source, string? tool = null) => source switch
    {
        WhereUsedSources.Scan => WhereUsedLabels.Source(source, tool),
        WhereUsedSources.Finding => "Excel bulgusu",
        WhereUsedSources.Manual => "Elle kayıt",
        WhereUsedSources.Spn => "SPN",
        _ => source
    };

    private static int Rank(string status) => status switch
    {
        WhereUsedStatuses.Conflict => 0,
        WhereUsedStatuses.Found => 1,
        WhereUsedStatuses.DefinedOnly => 2,
        WhereUsedStatuses.NotFoundInScanned => 3,
        _ => 4
    };

    private static int SourceRank(string source) => source switch
    {
        WhereUsedSources.Scan => 0,
        WhereUsedSources.Finding => 1,
        WhereUsedSources.Manual => 2,
        _ => 3
    };

    private static WhereUsedServer Row(IGrouping<string, Raw> group)
    {
        Raw[] items = [.. group];
        WhereUsedEvidence[] evidence = [.. items.Select(i => i.Evidence).OrderBy(e => SourceRank(e.Source)).ThenByDescending(e => e.At ?? DateTimeOffset.MinValue)];
        bool found = evidence.Any(e => e.Outcome == WhereUsedOutcomes.Found);
        bool notFound = evidence.Any(e => e.Outcome == WhereUsedOutcomes.NotFound);
        bool conflict = found && notFound;
        string status = conflict ? WhereUsedStatuses.Conflict
            : found ? WhereUsedStatuses.Found
            : notFound ? WhereUsedStatuses.NotFoundInScanned
            : evidence.Any(e => e.Outcome == WhereUsedOutcomes.Defined) ? WhereUsedStatuses.DefinedOnly
            : WhereUsedStatuses.NoInformation;
        DateTimeOffset? lastSeen = evidence.Where(e => e.Outcome == WhereUsedOutcomes.Found).Max(e => e.At);
        DateTimeOffset? lastChecked = evidence.Where(e => e.Source is WhereUsedSources.Scan or WhereUsedSources.Finding).Max(e => e.At);

        ServerNameKey display = items.FirstOrDefault(i => i.Name.Key == group.Key)?.Name ?? items[0].Name;
        string[] alsoSeen = [.. items.Select(i => i.Name).Where(n => n.Key != group.Key).GroupBy(n => n.Key, StringComparer.Ordinal).Select(g => g.First().Original)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)];
        string? conflictText = !conflict ? null
            : $"Çelişki: {Sources(evidence, WhereUsedOutcomes.Found)} sunucuda buldu; {Sources(evidence, WhereUsedOutcomes.NotFound)} tam taramada bulamadı. "
            + "Hangisinin güncel olduğuna tarihlerden bakın; bu, hesabın kullanılmadığını göstermez.";
        return new WhereUsedServer(display.Original, alsoSeen, status, StatusLabel(status), found, conflict, lastSeen, lastChecked,
            evidence.Select(e => e.Source).Distinct(StringComparer.Ordinal).Count(), conflictText, evidence);
    }

    private static string Sources(IEnumerable<WhereUsedEvidence> evidence, string outcome) => string.Join(", ", evidence.Where(e => e.Outcome == outcome)
        .Select(e => SourceLabel(e.Source, e.Tool)).Distinct(StringComparer.Ordinal));

    private static IEnumerable<Raw> ScanEvidence(WhereUsedData data)
    {
        var scans = data.Scans.ToDictionary(s => s.LinkId);
        ILookup<(Guid, string), WhereUsedScanItemRow> items = data.ScanItems.ToLookup(i => (i.LinkId, i.ServerName));
        foreach (WhereUsedScanServerRow server in data.ScanServers)
        {
            if (!scans.TryGetValue(server.LinkId, out WhereUsedScanRow? scan) || WhereUsedServerNames.Normalize(server.ServerName) is not { } name)
            {
                continue;
            }

            bool success = server.Result == nameof(ScanServerResult.Success);
            string outcome = server.Matches > 0 ? WhereUsedOutcomes.Found : success ? WhereUsedOutcomes.NotFound : WhereUsedOutcomes.NoInformation;
            WhereUsedComponent[] components = [.. items[(server.LinkId, server.ServerName)].Take(WhereUsedPaging.MaxComponentsPerEvidence)
                .Select(i => new WhereUsedComponent(i.ComponentType, UsageScanOutcomes.ComponentLabel(i.ComponentType), i.ComponentName))];
            string how = ToolLabel(scan.Tool);
            string text = outcome switch
            {
                WhereUsedOutcomes.Found => $"{how}: hesap bu sunucuda {server.Matches} bileşende çalışıyor.",
                WhereUsedOutcomes.NotFound => $"{how}: tam taramada bulunmadı (kullanılmıyor demek değildir).",
                _ => server.Result == nameof(ScanServerResult.Partial)
                    ? $"{how}: kısmi tarama, eşleşme yok; okunamayan kaynakta olabilir."
                    : $"{how}: bu sunucu için bilgi yok ({ResultText(server.Result)})."
            };
            yield return new Raw(name, new WhereUsedEvidence(WhereUsedSources.Scan, outcome, server.ScannedAt ?? scan.CombinedAt, scan.Tool, server.Result, text,
                components, server.Matches));
        }
    }

    private static string ResultText(string result) => Enum.TryParse(result, out ScanServerResult parsed)
        ? UsageScanOutcomes.ResultLabel(parsed).ToLowerInvariant()
        : "sonuç yok";

    private static IEnumerable<Raw> FindingEvidence(WhereUsedData data)
    {
        foreach (IGrouping<(string Key, string Outcome), WhereUsedFindingRow> group in data.Findings.Where(f => WhereUsedServerNames.Normalize(f.Server) is not null)
            .GroupBy(f => (Key: WhereUsedServerNames.Normalize(f.Server)!.Value.Key, Outcome: FindingOutcome(f))))
        {
            WhereUsedFindingRow[] rows = [.. group];
            WhereUsedComponent[] components = [.. rows.Where(r => r.ComponentType is not null || r.ComponentName is not null)
                .Select(r => new WhereUsedComponent(r.ComponentType ?? string.Empty, r.ComponentType ?? "Bileşen", r.ComponentName ?? string.Empty))
                .DistinctBy(c => (c.Type, c.Name)).Take(WhereUsedPaging.MaxComponentsPerEvidence)];
            string text = group.Key.Outcome switch
            {
                WhereUsedOutcomes.Found => "Excel bulgusu: bu sunucuda eşleşme var.",
                WhereUsedOutcomes.NotFound => "Excel bulgusu: tam taramada eşleşme yok (kullanılmıyor demek değildir).",
                _ => "Excel bulgusu: eşleşme belirsiz veya tarama tam değil."
            };
            yield return new Raw(WhereUsedServerNames.Normalize(rows[0].Server)!.Value, new WhereUsedEvidence(WhereUsedSources.Finding, group.Key.Outcome,
                rows.Max(r => r.At), null, null, text, components, rows.Length));
        }
    }

    private static string FindingOutcome(WhereUsedFindingRow row) => row.MatchResult == "Match" ? WhereUsedOutcomes.Found
        : row.MatchResult == "NoMatch" && row.ScanResult == "Success" ? WhereUsedOutcomes.NotFound
        : WhereUsedOutcomes.NoInformation;

    private static IEnumerable<Raw> ManualEvidence(WhereUsedData data)
    {
        foreach (IGrouping<string, WhereUsedUsageRow> group in data.Usages.Where(u => WhereUsedServerNames.Normalize(u.Server) is not null)
            .GroupBy(u => WhereUsedServerNames.Normalize(u.Server)!.Value.Key, StringComparer.Ordinal))
        {
            WhereUsedUsageRow[] rows = [.. group];
            WhereUsedComponent[] components = [.. rows.Select(r => new WhereUsedComponent(r.UsageKind, KindLabel(r.UsageKind), r.Component ?? string.Empty))
                .DistinctBy(c => (c.Type, c.Name)).Take(WhereUsedPaging.MaxComponentsPerEvidence)];
            yield return new Raw(WhereUsedServerNames.Normalize(rows[0].Server)!.Value, new WhereUsedEvidence(WhereUsedSources.Manual, WhereUsedOutcomes.Found,
                rows.Max(r => r.At), null, null, "Elle kayıt: bir kişi bu sunucuda kullanım kaydetti.", components, rows.Length));
        }
    }

    private static string KindLabel(string kind) => kind switch
    {
        "Database" => "Veritabanı",
        "FileShare" => "Dosya paylaşımı",
        "UncApplication" => "UNC uygulaması",
        "Other" => "Diğer",
        _ => UsageScanOutcomes.ComponentLabel(kind)
    };

    private static IEnumerable<Raw> SpnEvidence(WhereUsedSpnInput spn)
    {
        foreach (IGrouping<string, SpnHost> group in spn.Hosts.Where(h => WhereUsedServerNames.Normalize(h.Host) is not null)
            .GroupBy(h => WhereUsedServerNames.Normalize(h.Host)!.Value.Key, StringComparer.Ordinal))
        {
            SpnHost[] hosts = [.. group];
            WhereUsedComponent[] components = [.. hosts.Select(h => h.Class).Distinct(StringComparer.OrdinalIgnoreCase).Take(WhereUsedPaging.MaxComponentsPerEvidence)
                .Select(c => new WhereUsedComponent("SpnClass", "SPN sınıfı", c))];
            yield return new Raw(WhereUsedServerNames.Normalize(hosts[0].Host)!.Value, new WhereUsedEvidence(WhereUsedSources.Spn, WhereUsedOutcomes.Defined, null, null, null,
                "Active Directory'de bu ana makine için SPN tanımlı. Tanım kullanım kanıtı değildir; ana makine bir takma ad olabilir.", components, hosts.Length));
        }
    }

    private static IReadOnlyList<WhereUsedSourceStatus> SourceStatuses(WhereUsedData data, WhereUsedSpnInput spn)
    {
        string scanState = !data.ScansInstalled ? WhereUsedSourceStates.NotInstalled
            : data.ScansTotal == 0 ? WhereUsedSourceStates.Empty
            : data.ScansTotal > data.Scans.Count || data.ItemsTruncated ? WhereUsedSourceStates.Partial
            : WhereUsedSourceStates.Available;
        string scanText = scanState switch
        {
            WhereUsedSourceStates.NotInstalled => "Tarama kayıtları bu ortamda kullanılamıyor (veritabanı güncellemesi 030 uygulanmamış).",
            WhereUsedSourceStates.Empty => "Bu hesaba bağlı tarama yok; tarama yüklenene kadar tarama kaynağından bilgi yoktur.",
            WhereUsedSourceStates.Partial => $"En yeni {data.Scans.Count} tarama okundu"
                + (data.ScansTotal > data.Scans.Count ? $"; {data.ScansTotal - data.Scans.Count} eski tarama dışarıda" : string.Empty)
                + (data.ItemsTruncated ? $"; bileşen listesi ilk {WhereUsedPaging.MaxComponents} kayıtla sınırlı (sayılar tam)" : string.Empty) + ".",
            _ => $"{data.Scans.Count} tarama okundu."
        };
        string findingText = data.Findings.Count == 0 ? "Sunucu adı olan Excel bulgusu yok." : $"{data.Findings.Count} sunucu adlı Excel bulgusu okundu.";
        if (data.FindingsWithoutServer > 0)
        {
            findingText += $" {data.FindingsWithoutServer} bulgunun sunucu adı yok, listelenmedi.";
        }

        string manualText = data.Usages.Count == 0 ? "Sunucu adı olan elle kullanım kaydı yok." : $"{data.Usages.Count} sunucu adlı elle kayıt okundu.";
        if (data.UsagesWithoutServer > 0)
        {
            manualText += $" {data.UsagesWithoutServer} kaydın sunucu adı yok, listelenmedi.";
        }

        return
        [
            new(WhereUsedSources.Scan, scanState, scanText),
            new(WhereUsedSources.Finding, data.Findings.Count == 0 ? WhereUsedSourceStates.Empty : WhereUsedSourceStates.Available, findingText),
            new(WhereUsedSources.Manual, data.Usages.Count == 0 ? WhereUsedSourceStates.Empty : WhereUsedSourceStates.Available, manualText),
            new(WhereUsedSources.Spn, spn.State, spn.Text)
        ];
    }
}
