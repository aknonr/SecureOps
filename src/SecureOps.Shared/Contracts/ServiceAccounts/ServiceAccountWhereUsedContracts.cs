namespace SecureOps.Shared.Contracts.ServiceAccounts;

/// <summary>Evidence sources of the "where is this account used" view. Open for later sources (DNA report, gMSA attributes, inventory).</summary>
public static class WhereUsedSources
{
    /// <summary>Uploaded or web usage scan (<c>Tool</c> says Combined or Jea).</summary>
    public const string Scan = "Scan";
    /// <summary>Excel "Teknik_Bulgular" finding.</summary>
    public const string Finding = "Finding";
    /// <summary>A person's manual usage record.</summary>
    public const string Manual = "Manual";
    /// <summary>Active Directory servicePrincipalName host: a definition, not a use.</summary>
    public const string Spn = "Spn";
}

/// <summary>What one source says about one server. Nothing here says "not used".</summary>
public static class WhereUsedOutcomes
{
    /// <summary>The source found the account on the server (scan match, finding match, manual record).</summary>
    public const string Found = "Found";
    /// <summary>A complete read of the source's sources did not find it (never "not used").</summary>
    public const string NotFound = "NotFound";
    /// <summary>The source has no usable answer for the server (partial, failed, unreachable, uncertain).</summary>
    public const string NoInformation = "NoInformation";
    /// <summary>An SPN names the host: defined there, not necessarily used.</summary>
    public const string Defined = "Defined";
}

/// <summary>Row status, strongest first: Conflict, Found, DefinedOnly, NotFoundInScanned, NoInformation.</summary>
public static class WhereUsedStatuses
{
    /// <summary>One source found it and another complete read did not.</summary>
    public const string Conflict = "Conflict";
    /// <summary>At least one source found it.</summary>
    public const string Found = "Found";
    /// <summary>Only an SPN names the host.</summary>
    public const string DefinedOnly = "DefinedOnly";
    /// <summary>Only complete reads that did not find it; says nothing about use.</summary>
    public const string NotFoundInScanned = "NotFoundInScanned";
    /// <summary>Only unanswered or partial reads.</summary>
    public const string NoInformation = "NoInformation";
}

/// <summary>State of one source for this account, so a missing source is never read as "no servers".</summary>
public static class WhereUsedSourceStates
{
    /// <summary>The source answered and has rows.</summary>
    public const string Available = "Available";
    /// <summary>The source answered and has nothing for this account.</summary>
    public const string Empty = "Empty";
    /// <summary>The source answered only in part (for example a truncated SPN list or omitted older scans).</summary>
    public const string Partial = "Partial";
    /// <summary>The source's tables are not installed in this database.</summary>
    public const string NotInstalled = "NotInstalled";
    /// <summary>The caller lacks the capability the source needs.</summary>
    public const string Denied = "Denied";
    /// <summary>The source is not configured on this host.</summary>
    public const string NotConfigured = "NotConfigured";
    /// <summary>The source could not be read now (or has no answer for this account).</summary>
    public const string Unknown = "Unknown";
}

/// <summary>One component the evidence names (scan match or finding/manual component).</summary>
public sealed record WhereUsedComponent(string Type, string TypeLabel, string Name);

/// <summary>
/// One source's statement about one server. <c>At</c> is when the source looked (scan time, finding time) or recorded it; the SPN
/// has no date. <c>Tool</c> is Combined or Jea for scans; <c>ServerResult</c> is the scan's result for the server.
/// </summary>
public sealed record WhereUsedEvidence(string Source, string Outcome, DateTimeOffset? At, string? Tool, string? ServerResult, string Text,
    IReadOnlyList<WhereUsedComponent> Components, int ComponentCount);

/// <summary>
/// One server (names normalised: case, trailing dot, an FQDN is joined with a short name only when exactly one FQDN carries it).
/// <c>LastSeenAt</c> is the newest "found" evidence, <c>LastCheckedAt</c> the newest look of any scan or finding.
/// </summary>
public sealed record WhereUsedServer(string Name, IReadOnlyList<string> AlsoSeenAs, string Status, string StatusLabel, bool Found, bool Conflict,
    DateTimeOffset? LastSeenAt, DateTimeOffset? LastCheckedAt, int SourceCount, string? ConflictText, IReadOnlyList<WhereUsedEvidence> Evidence);

/// <summary>Why a source is or is not on the page.</summary>
public sealed record WhereUsedSourceStatus(string Source, string State, string Text);

/// <summary>Counts over every server of the account (not the page or the filter).</summary>
public sealed record WhereUsedSummary(int Servers, int Found, int Conflicts, int DefinedOnly, int NotFoundInScanned, int NoInformation, DateTimeOffset? LastScanAt,
    int ScansIncluded, int ScansOmitted, int UsagesWithoutServer, int FindingsWithoutServer, bool ComponentsTruncated);

/// <summary>
/// "On which servers is this account used": one row per server, with what each source says. Evidence only; no row, count or
/// filter says an account is unused, and the view cannot be read the other way round (server to accounts).
/// </summary>
public sealed record WhereUsedView(Guid AccountId, IReadOnlyList<WhereUsedServer> Servers, int Total, int Page, int PageSize, WhereUsedSummary Summary,
    IReadOnlyList<WhereUsedSourceStatus> Sources);

/// <summary>Paging and filter bounds of the where-used view.</summary>
public static class WhereUsedPaging
{
    /// <summary>Servers per page when the caller does not ask for another size.</summary>
    public const int DefaultPageSize = 50;
    /// <summary>Largest page.</summary>
    public const int MaxPageSize = 100;
    /// <summary>Newest scans of the account that are read (older ones are counted as omitted).</summary>
    public const int MaxScans = 20;
    /// <summary>Most component rows read across those scans; the per-server counts stay exact.</summary>
    public const int MaxComponents = 3000;
    /// <summary>Most components listed on one evidence line.</summary>
    public const int MaxComponentsPerEvidence = 25;
}

/// <summary>Turkish labels shared by the server texts and the UI badges so both say the same thing.</summary>
public static class WhereUsedLabels
{
    /// <summary>Short name of a source; a scan carries its tool (Combined = a person-run script uploaded, Jea = started from the web).</summary>
    public static string Source(string source, string? tool = null) => source switch
    {
        WhereUsedSources.Scan => tool == "Jea" ? "Tarama-Web" : "Tarama-Betik",
        WhereUsedSources.Finding => "Excel",
        WhereUsedSources.Manual => "Elle",
        WhereUsedSources.Spn => "SPN",
        _ => source
    };

    /// <summary>Long name of a scan tool, for the scan panel and texts.</summary>
    public static string Tool(string tool) => tool switch
    {
        "Jea" => "Web taraması (JEA)",
        "Combined" => "Betik çıktısı (yükleme)",
        _ => tool
    };

    /// <summary>What a source said, in one or two words; "bulunamadı" never means "kullanılmıyor".</summary>
    public static string Outcome(string outcome) => outcome switch
    {
        WhereUsedOutcomes.Found => "bulundu",
        WhereUsedOutcomes.NotFound => "tam taramada bulunamadı",
        WhereUsedOutcomes.Defined => "SPN tanımlı",
        _ => "bilgi yok"
    };
}
