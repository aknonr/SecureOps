namespace SecureOps.Shared.Contracts.ServiceAccounts;

/// <summary>Whether a web usage scan can be started on this installation, and why not (USAGE-SCAN-JOB-CONTRACT.md).</summary>
/// <param name="Available">True when the server accepts a start.</param>
/// <param name="Reason">Stable reason code when not available (<c>collectorNotConfigured</c>, <c>fakeNotAllowed</c>, <c>queueNotConfigured</c>, <c>notInstalled</c>, <c>moduleDisabled</c>).</param>
/// <param name="ReasonText">Turkish explanation for the reason; empty when available.</param>
/// <param name="Collector"><c>None</c>, <c>Fake</c> or <c>Jea</c>.</param>
/// <param name="IsFake">True when results come from the fake collector (no server is contacted).</param>
/// <param name="MaxAccounts">Most accounts per scan.</param>
/// <param name="MaxServers">Most planned servers per scan.</param>
/// <param name="CanStart">The caller holds ServiceAccounts.Work (scope is still checked per account on start).</param>
public sealed record UsageScanJobAvailabilityView(bool Available, string? Reason, string ReasonText, string Collector, bool IsFake, int MaxAccounts,
    int MaxServers, bool CanStart);

/// <summary>Start a web usage scan: 1–20 accounts the caller is responsible for, 1–500 planned servers, optional expected gMSA (a gMSA check).</summary>
/// <param name="AccountIds">Distinct account ids.</param>
/// <param name="Servers">Planned server names, one per entry (blank entries and repeats are dropped; each must match the scan pattern).</param>
/// <param name="ExpectedAccount">Expected gMSA ending with <c>$</c>, or null for a discovery scan.</param>
public sealed record StartUsageScanJobRequest(IReadOnlyList<Guid>? AccountIds, IReadOnlyList<string>? Servers, string? ExpectedAccount);

/// <summary>Live state of one web usage scan. Poll with <c>after</c> = <see cref="LastSeq"/> to receive only newly found items.</summary>
/// <param name="Id">Job id.</param>
/// <param name="State"><c>Queued</c>, <c>Running</c>, <c>Completed</c>, <c>Cancelled</c> or <c>Failed</c>.</param>
/// <param name="StateLabel">Turkish state label.</param>
/// <param name="Purpose"><c>Discovery</c> or <c>GmsaCheck</c>.</param>
/// <param name="ExpectedAccount">Expected gMSA of a gMSA check.</param>
/// <param name="Collector"><c>Fake</c> or <c>Jea</c>.</param>
/// <param name="IsFake">True when no server was contacted (fake collector).</param>
/// <param name="CreatedAt">When it was started.</param>
/// <param name="StartedAt">When the Worker claimed it.</param>
/// <param name="FinishedAt">When it ended.</param>
/// <param name="CancelRequested">A cancel was requested.</param>
/// <param name="FailureCode">Stable failure code of a failed job.</param>
/// <param name="FailureText">Turkish explanation of the failure.</param>
/// <param name="RetryOfJobId">The job this one retries.</param>
/// <param name="ScanId">The evidence scan written at the end (shown on each account's usage-scan tab).</param>
/// <param name="StartedByYou">The caller started it.</param>
/// <param name="Accounts">Accounts and how the evidence was linked.</param>
/// <param name="Servers">Every planned server in order, with its live tile.</param>
/// <param name="Counts">Aggregates for the progress bar and summary.</param>
/// <param name="EstimatedSecondsRemaining">Remaining-time estimate from the observed rate; null until enough servers finished.</param>
/// <param name="NewItems">Matched components of servers finished after the requested sequence.</param>
/// <param name="LastSeq">Highest finish sequence included in <see cref="NewItems"/> (or the requested one when none).</param>
/// <param name="MoreItems">More new items exist; poll again with <see cref="LastSeq"/>.</param>
/// <param name="Summary">End-of-scan sentence; null while running.</param>
/// <param name="NextStep">"Sıradaki adım" hint.</param>
/// <param name="Permissions">What the caller may do now.</param>
public sealed record UsageScanJobView(Guid Id, string State, string StateLabel, string Purpose, string? ExpectedAccount, string Collector, bool IsFake,
    DateTimeOffset CreatedAt, DateTimeOffset? StartedAt, DateTimeOffset? FinishedAt, bool CancelRequested, string? FailureCode, string? FailureText,
    Guid? RetryOfJobId, Guid? ScanId, bool StartedByYou, IReadOnlyList<UsageScanJobAccountView> Accounts, IReadOnlyList<UsageScanJobServerView> Servers,
    UsageScanJobCounts Counts, int? EstimatedSecondsRemaining, IReadOnlyList<UsageScanJobItemView> NewItems, int LastSeq, bool MoreItems, string? Summary,
    string NextStep, UsageScanJobPermissions Permissions);

/// <summary>One account of a scan.</summary>
/// <param name="AccountId">Account id.</param>
/// <param name="SearchedName">Name the collector searched.</param>
/// <param name="LinkOutcome"><c>Attached</c>, <c>AlreadyAttached</c>, <c>Unavailable</c>, <c>Failed</c>, <c>NotLinked</c>; null while running.</param>
/// <param name="LinkText">Turkish text of the link outcome.</param>
public sealed record UsageScanJobAccountView(Guid AccountId, string SearchedName, string? LinkOutcome, string? LinkText);

/// <summary>One planned server tile. <see cref="Tile"/> carries the meaning; colour is only a second signal in the UI.</summary>
/// <param name="Ordinal">Position in the plan (1-based).</param>
/// <param name="ServerName">Planned server name.</param>
/// <param name="State"><c>Pending</c>, <c>Connecting</c> or <c>Done</c>.</param>
/// <param name="Status">Collector status when done (<c>Ok</c>, <c>Unreachable</c>, <c>NoEndpoint</c>, <c>Forbidden</c>, <c>Timeout</c>, <c>Malformed</c>, <c>NotRun</c>).</param>
/// <param name="Tile"><c>Pending</c>, <c>Connecting</c>, <c>Found</c>, <c>NotFound</c>, <c>Uncertain</c> or <c>NotCovered</c>.</param>
/// <param name="TileText">Turkish tile text; "bulunamadı" is always qualified and never "kullanılmıyor".</param>
/// <param name="FormerMatches">Components running as a searched account.</param>
/// <param name="ExpectedMatches">Components running as the expected gMSA (gMSA check).</param>
/// <param name="ElapsedMs">Time spent on the server.</param>
/// <param name="DocumentResult"><c>Success</c>, <c>Partial</c> or <c>Failed</c> for an Ok answer.</param>
public sealed record UsageScanJobServerView(int Ordinal, string ServerName, string State, string? Status, string Tile, string TileText, int FormerMatches,
    int ExpectedMatches, int? ElapsedMs, string? DocumentResult);

/// <summary>Aggregates of a scan.</summary>
/// <param name="Planned">Planned servers.</param>
/// <param name="Pending">Waiting.</param>
/// <param name="Connecting">Being read.</param>
/// <param name="Done">Finished (any status).</param>
/// <param name="Found">Servers with at least one match.</param>
/// <param name="NotFound">Completely read, nothing found (never "not used").</param>
/// <param name="Uncertain">Partially read without a match.</param>
/// <param name="NotCovered">No information.</param>
/// <param name="Matches">Matched components of the searched accounts.</param>
/// <param name="Retryable">Finished servers a retry would repeat.</param>
public sealed record UsageScanJobCounts(int Planned, int Pending, int Connecting, int Done, int Found, int NotFound, int Uncertain, int NotCovered, int Matches,
    int Retryable);

/// <summary>A matched component found during the scan (live list).</summary>
/// <param name="Seq">Finish sequence of its server.</param>
/// <param name="ServerName">Server.</param>
/// <param name="Role"><c>Former</c> (a searched account) or <c>Expected</c> (the gMSA).</param>
/// <param name="MatchedAccount">Searched account or gMSA it runs as.</param>
/// <param name="ComponentType">Component type.</param>
/// <param name="ComponentLabel">Turkish component label.</param>
/// <param name="ComponentName">Component name.</param>
/// <param name="Identity">Configured identity.</param>
/// <param name="State">Reported state.</param>
/// <param name="Detail">Detail such as a physical path.</param>
public sealed record UsageScanJobItemView(int Seq, string ServerName, string Role, string MatchedAccount, string ComponentType, string ComponentLabel,
    string ComponentName, string Identity, string? State, string? Detail);

/// <summary>What the caller may do with a scan now (the server decides again on every command).</summary>
/// <param name="Cancel">The starter or an administrator, while it runs.</param>
/// <param name="Retry">Finished, with servers to repeat, and the caller may start a scan for every account.</param>
public sealed record UsageScanJobPermissions(bool Cancel, bool Retry);

/// <summary>A scan in an account's list.</summary>
/// <param name="Id">Job id.</param>
/// <param name="State">Job state.</param>
/// <param name="StateLabel">Turkish label.</param>
/// <param name="Active">Queued or running.</param>
/// <param name="CreatedAt">Started.</param>
/// <param name="FinishedAt">Ended.</param>
/// <param name="Planned">Planned servers.</param>
/// <param name="Done">Finished servers.</param>
/// <param name="Matches">Matched components (all searched accounts).</param>
/// <param name="NotCovered">Servers without information.</param>
/// <param name="IsFake">Fake collector.</param>
/// <param name="StartedByYou">The caller started it.</param>
public sealed record UsageScanJobSummaryView(Guid Id, string State, string StateLabel, bool Active, DateTimeOffset CreatedAt, DateTimeOffset? FinishedAt,
    int Planned, int Done, int Matches, int NotCovered, bool IsFake, bool StartedByYou);
