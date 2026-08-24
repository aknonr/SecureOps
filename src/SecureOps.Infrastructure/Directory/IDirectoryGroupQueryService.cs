using SecureOps.Shared.Contracts.Directory;

namespace SecureOps.Infrastructure.DirectoryExplorer;

/// <summary>Application boundary for bounded read-only Directory Explorer queries.</summary>
public interface IDirectoryGroupQueryService
{
    /// <summary>Returns one exact principal's direct groups.</summary>
    public Task<DirectoryQueryResult<DirectoryGroupPageResponse>> GetPrincipalGroupsAsync(
        DirectoryPrincipalGroupsRequest request, DirectoryQueryExecutionContext context, CancellationToken cancellationToken);
    /// <summary>Returns one exact group's safe metadata.</summary>
    public Task<DirectoryQueryResult<DirectoryGroupDetailResponse>> GetGroupAsync(
        DirectoryGroupLookupRequest request, DirectoryQueryExecutionContext context, CancellationToken cancellationToken);
    /// <summary>Returns one exact group's direct members.</summary>
    public Task<DirectoryQueryResult<DirectoryMemberPageResponse>> GetGroupMembersAsync(
        DirectoryGroupMembersRequest request, DirectoryQueryExecutionContext context, CancellationToken cancellationToken);
}

/// <summary>Safe HTTP-origin query context.</summary>
public sealed record DirectoryQueryExecutionContext(string Actor, string? SourceIp, string CorrelationId);

/// <summary>Directory query outcome.</summary>
public enum DirectoryQueryStatus
{
    /// <summary>Query completed.</summary>
    Success,
    /// <summary>Exact target was not found.</summary>
    NotFound,
    /// <summary>Input was rejected.</summary>
    Invalid,
    /// <summary>Provider was unavailable or timed out.</summary>
    ProviderUnavailable,
    /// <summary>The provider exceeded its configured timeout.</summary>
    ProviderTimeout,
    /// <summary>Provider result ceiling was exceeded.</summary>
    LimitExceeded,
    /// <summary>Required audit could not be persisted.</summary>
    AuditUnavailable
}

/// <summary>Value-or-safe-failure directory query result.</summary>
public sealed record DirectoryQueryResult<T>(DirectoryQueryStatus Status, T? Value, string? ErrorCode)
{
    /// <summary>Creates a successful result.</summary>
    public static DirectoryQueryResult<T> Success(T value) => new(DirectoryQueryStatus.Success, value, null);
    /// <summary>Creates a safe failure.</summary>
    public static DirectoryQueryResult<T> Failure(DirectoryQueryStatus status, string errorCode) => new(status, default, errorCode);
}
