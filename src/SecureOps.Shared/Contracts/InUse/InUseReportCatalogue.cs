namespace SecureOps.Shared.Contracts.InUse;

/// <summary>Searches verified archive metadata; dates are UTC half-open preparation instants.</summary>
public sealed record InUseReportQuery(string? Search = null, DateTimeOffset? From = null,
    DateTimeOffset? To = null, long? Version = null, string? Status = null, int Page = 1, int PageSize = 25);

/// <summary>Frozen original metadata plus explicitly current lifecycle/attachment projections.</summary>
public sealed record InUseReportEntry(Guid RecordId, long Version, long CurrentRecordVersion,
    string? SourceCode, IReadOnlyList<string> Hostnames, Guid PreparedBy, string? PreparedByLabel,
    string? PreparedByAccount, DateTimeOffset? PreparedAt, string FileName, string Sha256,
    string Status, string AttachmentStatus);

/// <summary>SQL bounded page and count from one transaction; historical indexing is explicitly incomplete.</summary>
public sealed record InUseReportPage(IReadOnlyList<InUseReportEntry> Items, long Total,
    int Page, int PageSize, DateTimeOffset AsOf, string Coverage);

/// <summary>Explicitly index selected retained versions, never regenerate or modify workbook bytes.</summary>
public sealed record IndexInUseReportsRequest(long ExpectedVersion, IReadOnlyList<long> Versions);

/// <summary>Already indexed versions remain committed on an interrupted batch; repeat the same selection.</summary>
public sealed record IndexInUseReportsResult(IReadOnlyList<long> IndexedVersions);
