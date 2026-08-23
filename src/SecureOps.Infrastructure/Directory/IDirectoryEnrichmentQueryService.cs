using SecureOps.Shared.Contracts.Directory;

namespace SecureOps.Infrastructure.DirectoryExplorer;

/// <summary>Application boundary for bounded read-only directory enrichment.</summary>
public interface IDirectoryEnrichmentQueryService
{
    /// <summary>Returns explicit direct and transitive groups.</summary>
    public Task<DirectoryQueryResult<DirectoryPrincipalMembershipsResponse>> GetMembershipsAsync(
        DirectoryPrincipalEnrichmentRequest request,
        DirectoryQueryExecutionContext context,
        CancellationToken cancellationToken);

    /// <summary>Returns bounded proven paths to one exact target group.</summary>
    public Task<DirectoryQueryResult<DirectoryMembershipPathResponse>> GetMembershipPathsAsync(
        DirectoryMembershipPathRequest request,
        DirectoryQueryExecutionContext context,
        CancellationToken cancellationToken);

    /// <summary>Returns safe account-health evidence.</summary>
    public Task<DirectoryQueryResult<DirectoryAccountHealthResponse>> GetAccountHealthAsync(
        DirectoryPrincipalEnrichmentRequest request,
        DirectoryQueryExecutionContext context,
        CancellationToken cancellationToken);

    /// <summary>Returns bounded SPN and service-account evidence.</summary>
    public Task<DirectoryQueryResult<DirectoryServiceEvidenceResponse>> GetServiceEvidenceAsync(
        DirectoryPrincipalEnrichmentRequest request,
        DirectoryQueryExecutionContext context,
        CancellationToken cancellationToken);

    /// <summary>Returns separately authorized configured privileged-group evidence.</summary>
    public Task<DirectoryQueryResult<DirectoryPrivilegedMembershipResponse>> GetPrivilegedMembershipsAsync(
        DirectoryPrincipalEnrichmentRequest request,
        DirectoryQueryExecutionContext context,
        CancellationToken cancellationToken);
}
