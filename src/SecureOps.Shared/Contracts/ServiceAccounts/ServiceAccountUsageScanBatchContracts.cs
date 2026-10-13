namespace SecureOps.Shared.Contracts.ServiceAccounts;

/// <summary>
/// One account's result of a multi-account usage-scan upload. <c>Outcome</c> is <c>Attached</c>, <c>AlreadyAttached</c>,
/// <c>NotInScan</c>, <c>Ambiguous</c>, <c>Unavailable</c> or <c>Failed</c>. <c>AccountName</c>/<c>Domain</c> are present only
/// for an account inside the caller's scope; <c>MatchedAccount</c> is the searched name of the file the link uses.
/// </summary>
public sealed record UsageScanBatchAccountResult(Guid AccountId, string? AccountName, string? Domain, string Outcome, string OutcomeLabel,
    string? MatchedAccount);

/// <summary>
/// Answer of a multi-account usage-scan upload (ADR-0027). The file was validated once, completely; each account was checked
/// on its own and one account's refusal never blocks another. <c>ScanId</c> is null when no account was linked (then nothing
/// from the file was stored). Results keep the requested order.
/// </summary>
public sealed record UsageScanBatchResult(Guid? ScanId, string Purpose, int PlannedServers, int AnsweredServers,
    IReadOnlyList<UsageScanBatchAccountResult> Results);
