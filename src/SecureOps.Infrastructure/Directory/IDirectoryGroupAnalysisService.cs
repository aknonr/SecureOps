using SecureOps.Shared.Contracts.Directory;

namespace SecureOps.Infrastructure.DirectoryExplorer;

/// <summary>Application boundary for bounded group analysis and export.</summary>
public interface IDirectoryGroupAnalysisService
{
    /// <summary>Returns bounded direct, effective, topology, and parent evidence.</summary>
    public Task<DirectoryQueryResult<DirectoryGroupAnalysisResponse>> AnalyzeAsync(
        DirectoryGroupAnalysisRequest request,
        DirectoryQueryExecutionContext context,
        CancellationToken cancellationToken);

    /// <summary>Creates one authorized bounded CSV payload.</summary>
    public Task<DirectoryQueryResult<DirectoryGroupExportResult>> ExportAsync(
        DirectoryGroupExportRequest request,
        DirectoryQueryExecutionContext context,
        CancellationToken cancellationToken);
}

/// <summary>Safe generated export payload.</summary>
public sealed record DirectoryGroupExportResult(
    byte[] Content,
    string ContentType,
    string FileName,
    string Mode,
    int RowCount);
