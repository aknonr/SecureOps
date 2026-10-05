namespace SecureOps.Domain.ServiceAccounts;

/// <summary>Result of one planned server in an uploaded usage scan (ADR-0027).</summary>
public enum ScanServerResult
{
    /// <summary>All three sources were read.</summary>
    Success,
    /// <summary>At least one source failed; the others were read.</summary>
    Partial,
    /// <summary>The collector ran but no source could be read.</summary>
    Failed,
    /// <summary>The person could not reach the server.</summary>
    Unreachable,
    /// <summary>The server was planned but no result was supplied.</summary>
    NoResult
}

/// <summary>What one scan says about one account on one server. Only <see cref="NotFound"/> follows a complete scan.</summary>
public enum ScanAccountOutcome
{
    /// <summary>At least one component runs as the account.</summary>
    Found,
    /// <summary>Complete scan of the sources it can read, no component: never "not used".</summary>
    NotFound,
    /// <summary>Partial scan without a component: a failed source may still hold one.</summary>
    Uncertain,
    /// <summary>No information (failed, unreachable or no result).</summary>
    NotCovered
}

/// <summary>Per-server state of a gMSA check for one former account.</summary>
public enum ScanGmsaServerState
{
    /// <summary>A component still runs as the former account.</summary>
    StillFormer,
    /// <summary>Complete scan: the former account is gone and a component runs as the gMSA.</summary>
    RunsAsGmsa,
    /// <summary>Complete scan: neither the former account nor the gMSA was found.</summary>
    NoComponents,
    /// <summary>Not covered, or partial without the former account: unknown.</summary>
    Unknown
}

/// <summary>Derived gMSA conversion evidence for one account across the planned servers. Evidence only, never verification.</summary>
public enum ScanGmsaConclusion
{
    /// <summary>The former account is still configured on at least one component.</summary>
    StillFormer,
    /// <summary>No former account found, but at least one planned server is not fully covered.</summary>
    Incomplete,
    /// <summary>Every planned server fully scanned, no former account left, the gMSA runs at least one component.</summary>
    ConvertedOnCoveredServers,
    /// <summary>Every planned server fully scanned and neither account was found.</summary>
    NoComponents
}

/// <summary>
/// Pure rules for reading an uploaded usage scan honestly (ADR-0027, SPEC rule 15): "not found" exists only after a complete
/// scan and never means "not used"; uncovered servers are "no information"; nothing here closes, frees or verifies.
/// </summary>
public static class UsageScanOutcomes
{
    /// <summary>Component types the collector reports.</summary>
    public static IReadOnlyList<string> ComponentTypes { get; } =
        ["WindowsService", "ScheduledTask", "IisAppPool", "IisSite", "IisApplication", "IisVirtualDirectory"];

    /// <summary>What the scan says about the account on one server.</summary>
    public static ScanAccountOutcome Outcome(ScanServerResult result, int formerMatches) => result switch
    {
        ScanServerResult.Success or ScanServerResult.Partial when formerMatches > 0 => ScanAccountOutcome.Found,
        ScanServerResult.Success => ScanAccountOutcome.NotFound,
        ScanServerResult.Partial => ScanAccountOutcome.Uncertain,
        _ => ScanAccountOutcome.NotCovered
    };

    /// <summary>gMSA check state on one server.</summary>
    public static ScanGmsaServerState GmsaState(ScanServerResult result, int formerMatches, int gmsaMatches) => result switch
    {
        ScanServerResult.Success or ScanServerResult.Partial when formerMatches > 0 => ScanGmsaServerState.StillFormer,
        ScanServerResult.Success when gmsaMatches > 0 => ScanGmsaServerState.RunsAsGmsa,
        ScanServerResult.Success => ScanGmsaServerState.NoComponents,
        _ => ScanGmsaServerState.Unknown
    };

    /// <summary>Conclusion over the planned servers; a server that is not fully covered can only make it weaker.</summary>
    public static ScanGmsaConclusion Conclusion(IEnumerable<ScanGmsaServerState> states)
    {
        ScanGmsaServerState[] all = [.. states];
        return all.Contains(ScanGmsaServerState.StillFormer) ? ScanGmsaConclusion.StillFormer
            : all.Length == 0 || all.Contains(ScanGmsaServerState.Unknown) ? ScanGmsaConclusion.Incomplete
            : all.Contains(ScanGmsaServerState.RunsAsGmsa) ? ScanGmsaConclusion.ConvertedOnCoveredServers
            : ScanGmsaConclusion.NoComponents;
    }

    /// <summary>
    /// Usage kind suggested for a matched component; the person chooses the final kind. A site or application whose files
    /// come from a UNC path is the knowledge-base "application runs from a UNC path" case.
    /// </summary>
    public static UsageKind SuggestedKind(string componentType, string? detail) => componentType switch
    {
        "WindowsService" => UsageKind.WindowsService,
        "ScheduledTask" => UsageKind.ScheduledTask,
        "IisAppPool" => UsageKind.IisAppPool,
        "IisVirtualDirectory" => UsageKind.IisVirtualDirectory,
        "IisSite" or "IisApplication" when detail is not null && detail.StartsWith(@"\\", StringComparison.Ordinal) => UsageKind.UncApplication,
        _ => UsageKind.Other
    };

    /// <summary>
    /// True when a searched account from the file (<c>name</c> or <c>DOMAIN\name</c>) refers to the module account. Names are
    /// compared by the module's account key; when both sides carry a domain, the domains must be equal too.
    /// </summary>
    public static bool NameMatches(string fileAccount, string accountName, string? accountDomain)
    {
        int slash = fileAccount.IndexOf('\\', StringComparison.Ordinal);
        string? fileDomain = slash > 0 ? fileAccount[..slash] : null;
        string fileName = slash >= 0 ? fileAccount[(slash + 1)..] : fileAccount;
        string? fileDomainKey = ServiceAccountText.DomainKey(fileDomain);
        string? accountDomainKey = ServiceAccountText.DomainKey(accountDomain);
        return ServiceAccountText.AccountKey(fileName) is { } key && key == ServiceAccountText.AccountKey(accountName)
            && (fileDomainKey is null || accountDomainKey is null || fileDomainKey == accountDomainKey);
    }

    /// <summary>
    /// The searched name of the file under which a scan is attached to the module account; the account then shows only the
    /// matches of that name. With a known domain the name qualified with that domain wins (a bare name would also show other
    /// domains' matches). With an unknown domain the bare name wins, because it matches every domain and so hides nothing;
    /// two names qualified with different domains and no bare name are <c>Ambiguous</c> (fail closed: the person adds the
    /// domain to the account or scans one name). Null when no searched name refers to the account.
    /// </summary>
    public static (string? Name, bool Ambiguous) SearchedName(IEnumerable<string> fileAccounts, string accountName, string? accountDomain)
    {
        string[] candidates = [.. fileAccounts.Where(a => NameMatches(a, accountName, accountDomain))];
        string? bare = candidates.FirstOrDefault(a => !a.Contains('\\', StringComparison.Ordinal));
        string[] qualified = [.. candidates.Where(a => a.Contains('\\', StringComparison.Ordinal))];
        if (ServiceAccountText.DomainKey(accountDomain) is not null)
        {
            return (qualified.FirstOrDefault() ?? bare, false);
        }

        return bare is not null || qualified.Length <= 1 ? (bare ?? qualified.FirstOrDefault(), false) : (null, true);
    }

    /// <summary>Turkish label of a server result.</summary>
    public static string ResultLabel(ScanServerResult value) => value switch
    {
        ScanServerResult.Success => "Tam tarandı",
        ScanServerResult.Partial => "Kısmi tarandı",
        ScanServerResult.Failed => "Kaynaklar okunamadı",
        ScanServerResult.Unreachable => "Erişilemedi",
        _ => "Sonuç yok"
    };

    /// <summary>Turkish label of an account outcome; "not found" is never worded as "not used".</summary>
    public static string OutcomeLabel(ScanAccountOutcome value) => value switch
    {
        ScanAccountOutcome.Found => "Bu sunucuda çalışıyor",
        ScanAccountOutcome.NotFound => "Taranan kaynaklarda bulunmadı (kullanılmıyor demek değildir)",
        ScanAccountOutcome.Uncertain => "Belirsiz: kısmi tarama, okunamayan kaynakta olabilir",
        _ => "Bilgi yok: sunucu taranamadı"
    };

    /// <summary>Turkish label of a gMSA server state.</summary>
    public static string GmsaStateLabel(ScanGmsaServerState value) => value switch
    {
        ScanGmsaServerState.StillFormer => "Eski hesap hâlâ yapılandırılı",
        ScanGmsaServerState.RunsAsGmsa => "gMSA ile çalışıyor, eski hesap yok",
        ScanGmsaServerState.NoComponents => "Ne eski hesap ne gMSA bulundu",
        _ => "Bilinmiyor: tam taranmadı"
    };

    /// <summary>Turkish label of a gMSA conclusion; it always says it is evidence, not verification.</summary>
    public static string ConclusionLabel(ScanGmsaConclusion value) => value switch
    {
        ScanGmsaConclusion.StillFormer => "Dönüşüm tamamlanmamış: eski hesap hâlâ en az bir bileşende",
        ScanGmsaConclusion.Incomplete => "Kanıt eksik: eski hesap bulunmadı ama taranamayan veya kısmi taranan sunucu var",
        ScanGmsaConclusion.ConvertedOnCoveredServers => "Taranan tüm sunucularda eski hesap kalmadı ve gMSA çalışıyor (kanıt; doğrulamayı doğrulayıcı yapar)",
        _ => "Kanıt yok: ne eski hesap ne gMSA bulundu"
    };

    /// <summary>Turkish label of a component type.</summary>
    public static string ComponentLabel(string componentType) => componentType switch
    {
        "WindowsService" => "Windows servisi",
        "ScheduledTask" => "Zamanlanmış görev",
        "IisAppPool" => "IIS uygulama havuzu",
        "IisSite" => "IIS sitesi",
        "IisApplication" => "IIS uygulaması",
        "IisVirtualDirectory" => "IIS sanal dizini",
        _ => "Bileşen"
    };
}
