using System.ComponentModel.DataAnnotations;
using SecureOps.Domain.OperationalRecords;

namespace SecureOps.Shared.Contracts.OperationalRecords;

/// <summary>Bounded persisted browsing; never refreshes an external source.</summary>
public sealed record OperationalRecordQuery
{
    /// <summary>Literal code or title substring.</summary>
    [StringLength(100)] public string? Search { get; init; }
    /// <summary>Optional persisted workflow state.</summary>
    [EnumDataType(typeof(OperationalRecordWorkflowState))] public OperationalRecordWorkflowState? State { get; init; }
    /// <summary>Closed stable sort with source identifier as tie breaker.</summary>
    [Required, RegularExpression("^(updated|oldest|code)$")] public string Sort { get; init; } = "updated";
    /// <summary>One-based page.</summary>
    [Range(1, 100000)] public int Page { get; init; } = 1;
    /// <summary>Maximum 100 rows per read.</summary>
    [Range(1, 100)] public int PageSize { get; init; } = 25;
}

/// <summary>Actual persisted matching count, not a source-system total.</summary>
public sealed record OperationalRecordPageResponse(IReadOnlyList<OperationalRecordResponse> Items, int Total, int Page, int PageSize);
