using SecureOps.Domain.ServiceAccounts;
using SecureOps.Shared.Contracts.ServiceAccounts;

namespace SecureOps.Infrastructure.ServiceAccounts;

/// <summary>Authenticated module actor for audit/history (application user ID, never a display name).</summary>
/// <param name="UserId">Application user.</param>
/// <param name="CorrelationId">Request correlation.</param>
public sealed record SaActor(Guid UserId, string CorrelationId);

/// <summary>Module result with safe error code, optional field and optional current state for 409 comparison.</summary>
public sealed record SaResult<T>(T? Value, string? ErrorCode = null, string? Field = null, object? Current = null)
{
    /// <summary>True on success.</summary>
    public bool IsSuccess => ErrorCode is null;

    /// <summary>Creates a failure.</summary>
    public static SaResult<T> Fail(string code, string? field = null, object? current = null) => new(default, code, field, current);

    /// <summary>Implicit success.</summary>
    public static implicit operator SaResult<T>(T value) => new(value);
}

/// <summary>Stable module error codes (mapped to HTTP status by the API).</summary>
public static class SaErrors
{
    /// <summary>Not found or not in scope (indistinguishable).</summary>
    public const string NotFound = "ServiceAccountNotFound";
    /// <summary>Capability or scope missing.</summary>
    public const string Forbidden = "ServiceAccountAccessDenied";
    /// <summary>Validation failed.</summary>
    public const string Invalid = "ServiceAccountValidationFailed";
    /// <summary>Stale version.</summary>
    public const string Conflict = "ServiceAccountConcurrencyConflict";
    /// <summary>Module not configured on this host.</summary>
    public const string NotConfigured = "ServiceAccountsNotConfigured";
    /// <summary>Persistence unavailable.</summary>
    public const string Unavailable = "ServiceAccountPersistenceUnavailable";
    /// <summary>The identity directory is not configured, timed out or failed.</summary>
    public const string DirectoryUnavailable = "ServiceAccountDirectoryUnavailable";
    /// <summary>Import file rejected.</summary>
    public const string ImportFile = "ServiceAccountImportFileRejected";
    /// <summary>Import preview is stale.</summary>
    public const string PreviewStale = "ServiceAccountImportPreviewStale";
    /// <summary>Import has undecided rows.</summary>
    public const string DecisionsRequired = "ServiceAccountImportDecisionsRequired";
    /// <summary>Import already committed for the same file/period/scope.</summary>
    public const string AlreadyImported = "ServiceAccountImportAlreadyCommitted";
    /// <summary>Idempotency key missing or invalid.</summary>
    public const string IdempotencyKey = "ServiceAccountIdempotencyKeyRequired";
}

/// <summary>Scope inputs loaded for one caller.</summary>
public sealed record ScopeData(IReadOnlyList<ScopeGrant> Grants, IReadOnlyList<OrganizationNode> Organizations, IReadOnlyList<TeamNode> Teams);

/// <summary>Stored import batch.</summary>
public sealed record ImportBatchRecord(Guid Id, string Profile, string FileName, string ContentType, string Sha256, byte[]? Content,
    DateOnly? SourceReportDate, string SourceDateProvenance, string? DeclaredScope, string? DeclaredDomain, string MappingJson, int MappingVersion,
    int PreviewVersion, int DecisionVersion, string Status, string ReplayKey, string? SummaryJson, string? ResultJson, string? CommitIdempotencyKey,
    Guid UploadedBy, DateTimeOffset UploadedAt, DateTimeOffset? CommittedAt);

/// <summary>Stored preview row.</summary>
public sealed record ImportRowRecord(int RowKey, string Sheet, int RowNumber, string EntityKind, string OriginalJson, string? NormalizedJson,
    string Classification, Guid? MatchAccountId, string? CandidatesJson, string? ErrorsJson, string? DiffJson, bool RequiresDecision,
    string? Decision, string? DecisionNote, Guid? DecidedBy, DateTimeOffset? DecidedAt);

/// <summary>Commit outcome.</summary>
public sealed record ImportCommitOutcome(ImportResultView Result, bool Replay);
