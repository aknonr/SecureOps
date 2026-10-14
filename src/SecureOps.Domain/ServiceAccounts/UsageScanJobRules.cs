using System.Text.RegularExpressions;

namespace SecureOps.Domain.ServiceAccounts;

/// <summary>Lifecycle of a web usage-scan job (ADR-0024 R2-4). Only Queued and Running hold the accounts.</summary>
public enum UsageScanJobState
{
    /// <summary>Accepted and handed to the job queue; nothing has been read yet.</summary>
    Queued,
    /// <summary>The Worker claimed it and servers are being read.</summary>
    Running,
    /// <summary>Every planned server reported; the evidence record is written.</summary>
    Completed,
    /// <summary>Stopped by a person; servers that finished are kept as evidence, the others are "çalışmadı".</summary>
    Cancelled,
    /// <summary>Could not run or finish (no collector, access revoked, Worker lost, evidence refused); see the failure code.</summary>
    Failed
}

/// <summary>Where one planned server of a running job is.</summary>
public enum UsageScanJobServerState
{
    /// <summary>Waiting for a free slot.</summary>
    Pending,
    /// <summary>The collector is connecting or reading.</summary>
    Connecting,
    /// <summary>Final status recorded.</summary>
    Done
}

/// <summary>What a finished server tile says, for colour, icon and text together (never colour alone).</summary>
public enum UsageScanTileKind
{
    /// <summary>Waiting.</summary>
    Pending,
    /// <summary>Connecting or reading.</summary>
    Connecting,
    /// <summary>At least one component runs as a searched account.</summary>
    Found,
    /// <summary>Complete read, nothing found: "aranan kaynaklarda bulunamadı", never "kullanılmıyor".</summary>
    NotFound,
    /// <summary>Partial read without a match: a failed source may still hold one.</summary>
    Uncertain,
    /// <summary>No information (not reached, no endpoint, refused, timed out, malformed, not run).</summary>
    NotCovered
}

/// <summary>
/// Pure rules of the web usage-scan job (USAGE-SCAN-JOB-CONTRACT.md): input bounds, the collector status → evidence mapping,
/// which servers a retry repeats, the remaining-time estimate and the Turkish texts. "Bulunamadı" exists only after a complete
/// read and never means "kullanılmıyor"; every status other than Ok is "bilgi yok" (SPEC rule 15).
/// </summary>
public static partial class UsageScanJobRules
{
    /// <summary>Most accounts per job (ADR-0024 R2-4).</summary>
    public const int MaxAccounts = 20;

    /// <summary>Most planned servers per job (ADR-0024 R2-4, same as an uploaded scan).</summary>
    public const int MaxServers = 500;

    [GeneratedRegex(@"^(?:[A-Za-z0-9][A-Za-z0-9_.-]{0,14}\\)?[A-Za-z0-9_][A-Za-z0-9_.-]{0,63}\$?\z", RegexOptions.CultureInvariant, 100)]
    private static partial Regex AccountPattern();

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9.-]{0,252}\z", RegexOptions.CultureInvariant, 100)]
    private static partial Regex ServerPattern();

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9_.-]{0,14}\z", RegexOptions.CultureInvariant, 100)]
    private static partial Regex NetBiosPattern();

    /// <summary>Same pattern as <c>service-account-usage-scan-v1</c> <c>accounts</c> (the parser refuses anything else).</summary>
    public static bool IsAccountName(string? value) => value is { Length: > 0 and <= 100 } && AccountPattern().IsMatch(value);

    /// <summary>Same pattern as <c>service-account-usage-scan-v1</c> <c>plannedServers</c>.</summary>
    public static bool IsServerName(string? value) => value is { Length: > 0 and <= 253 } && ServerPattern().IsMatch(value);

    /// <summary>
    /// The name the collector searches for an account: <c>DOMAIN\name</c> when the recorded domain is a NetBIOS-style name, otherwise
    /// the bare name (it matches every domain, so nothing is hidden). Null when the name cannot be searched safely.
    /// </summary>
    public static string? SearchName(string accountName, string? domain)
    {
        string name = accountName.Trim();
        int slash = name.IndexOf('\\', StringComparison.Ordinal);
        if (slash >= 0)
        {
            domain ??= name[..slash];
            name = name[(slash + 1)..];
        }

        string? qualified = domain is { } d && NetBiosPattern().IsMatch(d.Trim()) ? $"{d.Trim()}\\{name}" : null;
        return qualified is not null && IsAccountName(qualified) ? qualified : IsAccountName(name) ? name : null;
    }

    /// <summary>
    /// Parses the pasted planned-server list: trimmed, blank lines and duplicates (case-insensitive) dropped, order kept. Error codes:
    /// <c>serversEmpty</c>, <c>serversTooMany</c>, <c>serverName</c> (with the first refused line number, 1-based).
    /// </summary>
    public static (IReadOnlyList<string> Servers, string? Error, int? Line) ParseServers(IEnumerable<string>? lines)
    {
        List<string> servers = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        int number = 0;
        foreach (string raw in lines ?? [])
        {
            number++;
            string value = (raw ?? string.Empty).Trim();
            if (value.Length == 0)
            {
                continue;
            }

            if (!IsServerName(value))
            {
                return ([], "serverName", number);
            }

            if (seen.Add(value))
            {
                servers.Add(value);
            }
        }

        return servers.Count == 0 ? ([], "serversEmpty", null)
            : servers.Count > MaxServers ? ([], "serversTooMany", null)
            : (servers, null, null);
    }

    /// <summary>Whether the state still holds its accounts (at most one such job per account).</summary>
    public static bool IsActive(UsageScanJobState state) => state is UsageScanJobState.Queued or UsageScanJobState.Running;

    /// <summary>How a collector status is stored in the evidence record: Ok is a document; Unreachable stays Unreachable; the rest is NoResult.</summary>
    public static ScanServerResult EvidenceReason(UsageCollectionStatus status) =>
        status == UsageCollectionStatus.Unreachable ? ScanServerResult.Unreachable : ScanServerResult.NoResult;

    /// <summary>
    /// Whether a retry repeats this server: everything that did not give a complete read (any status but Ok, or Ok with a Partial or
    /// Failed document). Complete reads are never repeated by a retry; a new scan repeats them.
    /// </summary>
    public static bool NeedsRetry(UsageCollectionStatus? status, ScanServerResult? documentResult) =>
        status != UsageCollectionStatus.Ok || documentResult is not ScanServerResult.Success;

    /// <summary>Tile meaning of one server, from its job state, status, document result and former-account matches.</summary>
    public static UsageScanTileKind Tile(UsageScanJobServerState state, UsageCollectionStatus? status, ScanServerResult? documentResult, int formerMatches) =>
        state switch
        {
            UsageScanJobServerState.Pending => UsageScanTileKind.Pending,
            UsageScanJobServerState.Connecting => UsageScanTileKind.Connecting,
            _ when status != UsageCollectionStatus.Ok || documentResult is null => UsageScanTileKind.NotCovered,
            _ => UsageScanOutcomes.Outcome(documentResult.Value, formerMatches) switch
            {
                ScanAccountOutcome.Found => UsageScanTileKind.Found,
                ScanAccountOutcome.NotFound => UsageScanTileKind.NotFound,
                ScanAccountOutcome.Uncertain => UsageScanTileKind.Uncertain,
                _ => UsageScanTileKind.NotCovered
            }
        };

    /// <summary>
    /// Remaining time from the observed rate (finished servers per elapsed time already reflects the parallelism). Null until three
    /// servers finished or when nothing remains: an early guess would be noise.
    /// </summary>
    public static TimeSpan? EstimateRemaining(DateTimeOffset? startedAt, DateTimeOffset now, int done, int total)
    {
        if (startedAt is not { } started || done < 3 || done >= total || now <= started)
        {
            return null;
        }

        double perServer = (now - started).TotalSeconds / done;
        return TimeSpan.FromSeconds(Math.Ceiling(perServer * (total - done)));
    }

    /// <summary>Short Turkish label of a collector status.</summary>
    public static string StatusLabel(UsageCollectionStatus status) => status switch
    {
        UsageCollectionStatus.Ok => "Okundu",
        UsageCollectionStatus.Unreachable => "Erişilemedi",
        UsageCollectionStatus.NoEndpoint => "Tarama uç noktası yok",
        UsageCollectionStatus.Forbidden => "Okuma yetkisi yok",
        UsageCollectionStatus.Timeout => "Zaman aşımı",
        UsageCollectionStatus.Malformed => "Yanıt bozuk",
        _ => "Çalışmadı"
    };

    /// <summary>Turkish tile text. "Bulunamadı" is always qualified; nothing here says "kullanılmıyor".</summary>
    public static string TileLabel(UsageScanTileKind kind, UsageCollectionStatus? status, int found) => kind switch
    {
        UsageScanTileKind.Pending => "Bekliyor",
        UsageScanTileKind.Connecting => "Bağlanıyor",
        UsageScanTileKind.Found => found == 1 ? "1 kullanım bulundu" : $"{found} kullanım bulundu",
        UsageScanTileKind.NotFound => "Aranan kaynaklarda bulunamadı",
        UsageScanTileKind.Uncertain => "Kısmi okundu, bulunamadı (belirsiz)",
        _ => $"{StatusLabel(status ?? UsageCollectionStatus.NotRun)} — bilgi yok"
    };

    /// <summary>Turkish label of a job state.</summary>
    public static string StateLabel(UsageScanJobState state) => state switch
    {
        UsageScanJobState.Queued => "Sırada",
        UsageScanJobState.Running => "Çalışıyor",
        UsageScanJobState.Completed => "Tamamlandı",
        UsageScanJobState.Cancelled => "İptal edildi",
        _ => "Başarısız"
    };

    /// <summary>Turkish text of a stable failure code (no internal detail).</summary>
    public static string FailureLabel(string? code) => code switch
    {
        "collectorUnavailable" => "Sunucu tarafı toplayıcı bu Worker'da kurulu değil; hiçbir sunucuya bağlanılmadı.",
        "accessRevoked" => "Başlatan kişinin yetkisi tarama başlamadan önce kalktı; hiçbir sunucuya bağlanılmadı.",
        "workerLost" => "Worker taramayı bitiremedi (yeniden başlatma veya kesinti); biten sunucular kanıta yazılamadı. Yeniden deneyin.",
        "collectorFailed" => "Toplayıcı beklenmedik biçimde durdu; bitmeyen sunucular \"çalışmadı\" sayıldı.",
        "evidenceRefused" => "Toplanan sonuç kanıt kaydı olarak kabul edilmedi; hiçbir hesaba bağlanmadı.",
        "dispatchFailed" => "İş kuyruğa verilemedi; tarama başlamadı.",
        "notStarted" => "Worker işi zamanında almadı (Worker çalışmıyor olabilir); hiçbir sunucuya bağlanılmadı.",
        null => string.Empty,
        _ => "Tarama tamamlanamadı."
    };

    /// <summary>
    /// The run statement stored with the evidence (030 <c>RunStatement</c>, 5–400 characters): where and under which authority the
    /// scan ran. A fake run says plainly that no server was contacted.
    /// </summary>
    public static string RunStatement(string collector, Guid jobId, bool cancelled)
    {
        string text = string.Equals(collector, "Fake", StringComparison.Ordinal)
            ? $"SAHTE toplayıcı (yerel gösterim/test): hiçbir sunucuya bağlanılmadı, sonuçlar gerçek değildir. Web taraması, iş {jobId:N}."
            : $"Ürün web taraması: Worker hizmeti, JEA uç noktası, okuma gMSA'sı, salt okunur. İş {jobId:N}.";
        return cancelled ? text + " İptal edildi: yalnız biten sunucular okundu." : text;
    }

    /// <summary>
    /// The end-of-scan sentence, e.g. "3 sunucuda 7 kullanım bulundu; 2 sunucuya erişilemedi veya okunamadı → yalnız onları yeniden
    /// deneyebilirsiniz." Uncovered servers are always named as "bilgi yok", never folded into "bulunamadı".
    /// </summary>
    public static string Summary(int serversWithMatches, int matches, int notFound, int uncertain, int notCovered)
    {
        List<string> parts = [];
        parts.Add(matches == 0 ? "Hiçbir sunucuda kullanım bulunmadı" : $"{serversWithMatches} sunucuda {matches} kullanım bulundu");
        if (notFound > 0)
        {
            parts.Add($"{notFound} sunucuda aranan kaynaklarda bulunamadı (kullanılmıyor demek değildir)");
        }

        if (uncertain > 0)
        {
            parts.Add($"{uncertain} sunucu kısmi okundu");
        }

        if (notCovered > 0)
        {
            parts.Add($"{notCovered} sunucuya erişilemedi veya okunamadı (bilgi yok)");
        }

        string text = string.Join("; ", parts);
        return notCovered + uncertain > 0 ? text + " → yalnız bunları yeniden deneyebilirsiniz." : text + ".";
    }

    /// <summary>"Sıradaki adım" hint for the current job state.</summary>
    public static string NextStep(UsageScanJobState state, int matches, int retryable, bool linked) => state switch
    {
        UsageScanJobState.Queued => "Sıradaki adım: Worker işi alınca sunucular tek tek dolmaya başlar. Bu sayfadan ayrılabilirsiniz; tarama sürer.",
        UsageScanJobState.Running => "Sıradaki adım: bulunan kullanımlar aşağıdaki listeye düşer. Tarama bitince sonuç hesabın Kullanım taraması bölümüne bağlanır.",
        _ when !linked => retryable > 0
            ? "Sıradaki adım: sonuç kanıta yazılamadı. Başarısız sunucuları yeniden deneyin veya script çıktısını yükleyin."
            : "Sıradaki adım: yeni bir tarama başlatın veya script çıktısını yükleyin.",
        _ when matches > 0 => "Sıradaki adım: hesabın Kullanım taraması bölümünde her bulguyu kullanım olarak kaydedin veya gerekçeyle ayırın."
            + (retryable > 0 ? " Bilgi alınamayan sunucuları yeniden deneyebilirsiniz." : string.Empty),
        _ when retryable > 0 => "Sıradaki adım: bilgi alınamayan sunucuları yeniden deneyin; bulunamadı, kullanılmıyor demek değildir.",
        _ => "Sıradaki adım: sonuç hesaba bağlandı. Bulunamadı, kullanılmıyor demek değildir; tarama görmediği yerleri (COM+, bağlantı dizeleri, veritabanı) ayrıca kontrol edin."
    };
}
