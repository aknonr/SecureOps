using SecureOps.Shared.Contracts.Directory;

namespace SecureOps.Ui.Services;

/// <summary>
/// Client for the read-only Directory Explorer endpoints in
/// <c>docs/contracts/secureops-api-v1-ui-integration.md</c>.
/// </summary>
/// <remarks>
/// <para>
/// Every call is exact-only: one account or one group, never a filter, a wildcard, or an LDAP
/// expression. Each is recorded, bounded, and rate-limited, so the UI loads each section on demand
/// rather than fetching everything the moment a lookup returns.
/// </para>
/// <para>
/// <c>purpose</c> is optional on every route here and is passed as <c>null</c> when the operator gave
/// none. It is context on the audit record, not permission to read: the query is recorded either
/// way, it never enters the cache or rate-limit identity, and the server re-validates its bounds.
/// </para>
/// <para>
/// Nothing here traverses a graph. Direct groups, transitive groups, and membership paths are all
/// computed server-side and arrive with traversal metadata describing the limits that applied;
/// recomputing or extending any of it in the browser would produce an answer nobody could audit.
/// </para>
/// </remarks>
public interface IDirectoryApiClient
{
    /// <summary>
    /// Reads one bounded page of a principal's <b>direct</b> groups.
    /// </summary>
    /// <param name="account">Exact account.</param>
    /// <param name="purpose">Optional operational context; <c>null</c> when none was given.</param>
    /// <param name="pageSize">Requested page size; the API caps it.</param>
    /// <param name="continuationToken">Opaque token from the previous page, or <c>null</c> to start.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Direct groups only — never nested ones.</returns>
    /// <remarks>The continuation token is opaque and server-protected; never parse or construct one.</remarks>
    public Task<DirectoryGroupPageResponse> GetPrincipalGroupsAsync(
        string account,
        string? purpose,
        int? pageSize,
        string? continuationToken,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reads a principal's direct and transitive memberships as two separate sets.
    /// </summary>
    /// <param name="account">Exact account.</param>
    /// <param name="purpose">Optional operational context; <c>null</c> when none was given.</param>
    /// <param name="refresh">Bypasses the server-side query cache.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Direct groups, transitive groups, and the traversal bounds that applied.</returns>
    /// <remarks>
    /// The two lists stay separate by contract. A group can appear in both when it is held directly
    /// and is also reachable through nesting; <c>alsoTransitivelyReachable</c> marks that case rather
    /// than duplicating the row.
    /// </remarks>
    public Task<DirectoryPrincipalMembershipsResponse> GetMembershipsAsync(
        string account,
        string? purpose,
        bool refresh,
        CancellationToken cancellationToken);

    /// <summary>
    /// Proves how a principal reaches one exact group, if it does.
    /// </summary>
    /// <param name="account">Exact account.</param>
    /// <param name="targetGroup">Exact group the caller wants explained.</param>
    /// <param name="purpose">Optional operational context; <c>null</c> when none was given.</param>
    /// <param name="refresh">Bypasses the server-side query cache.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Membership verdict, the proven chains, and traversal bounds.</returns>
    /// <remarks>
    /// <c>isMember=false</c> is conclusive <b>only</b> when traversal reports no limit and no
    /// truncation. Presenting a bounded traversal as "not a member" would be an authorization answer
    /// the backend never gave.
    /// </remarks>
    public Task<DirectoryMembershipPathResponse> GetMembershipPathsAsync(
        string account,
        string targetGroup,
        string? purpose,
        bool refresh,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reads operational account-health evidence.
    /// </summary>
    /// <param name="account">Exact account.</param>
    /// <param name="purpose">Optional operational context; <c>null</c> when none was given.</param>
    /// <param name="refresh">Bypasses the server-side query cache.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Nullable evidence; every field can legitimately be unknown.</returns>
    /// <remarks>
    /// <c>lastLogonTimestampUtc</c> is the replicated attribute, not an exact sign-in time, and the
    /// response says so through <c>lastLogonTimestampIsApproximate</c>. It must never be labelled as
    /// a precise last logon.
    /// </remarks>
    public Task<DirectoryAccountHealthResponse> GetAccountHealthAsync(
        string account,
        string? purpose,
        bool refresh,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reads bounded SPN and directory account-type evidence.
    /// </summary>
    /// <param name="account">Exact account.</param>
    /// <param name="purpose">Optional operational context; <c>null</c> when none was given.</param>
    /// <param name="refresh">Bypasses the server-side query cache.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>SPNs, counts, truncation state, and non-authoritative account-type evidence.</returns>
    /// <remarks>
    /// <c>accountTypeEvidence</c> is evidence, not a classification. The API does not decide what is
    /// a service account, and neither may the UI.
    /// </remarks>
    public Task<DirectoryServiceEvidenceResponse> GetServiceEvidenceAsync(
        string account,
        string? purpose,
        bool refresh,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reads privileged-group membership evidence for the server-configured group set.
    /// </summary>
    /// <param name="account">Exact account.</param>
    /// <param name="purpose">Optional operational context; <c>null</c> when none was given.</param>
    /// <param name="refresh">Bypasses the server-side query cache.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>One entry per configured group, with direct/transitive state and proven paths.</returns>
    /// <remarks>
    /// Requires <c>Identity.PrivilegedGroups.View</c>, which is separate from ordinary group viewing.
    /// The configured group set is server-owned; the UI neither supplies nor infers it.
    /// </remarks>
    public Task<DirectoryPrivilegedMembershipResponse> GetPrivilegedMembershipsAsync(
        string account,
        string? purpose,
        bool refresh,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reads exact group metadata.
    /// </summary>
    /// <param name="group">Exact group identifier.</param>
    /// <param name="purpose">Optional operational context; <c>null</c> when none was given.</param>
    /// <param name="refresh">Bypasses the server-side query cache.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Group metadata including scope, category, manager, and direct member count.</returns>
    public Task<DirectoryGroupDetailResponse> GetGroupAsync(
        string group,
        string? purpose,
        bool refresh,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reads one bounded page of a group's <b>direct</b> members.
    /// </summary>
    /// <param name="group">Exact group identifier.</param>
    /// <param name="purpose">Optional operational context; <c>null</c> when none was given.</param>
    /// <param name="pageSize">Requested page size; the API caps it.</param>
    /// <param name="continuationToken">Opaque token from the previous page, or <c>null</c> to start.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Direct members only, of every member type.</returns>
    /// <remarks>
    /// Requires <c>Identity.Groups.Members.View</c>, a different capability from group metadata.
    /// Nested groups appear as members; they are not expanded, here or in the browser.
    /// </remarks>
    public Task<DirectoryMemberPageResponse> GetGroupMembersAsync(
        string group,
        string? purpose,
        int? pageSize,
        string? continuationToken,
        CancellationToken cancellationToken);

    /// <summary>
    /// Runs the bounded nested-group analysis for one exact group.
    /// </summary>
    /// <param name="group">Exact group identifier.</param>
    /// <param name="purpose">Optional operational context.</param>
    /// <param name="refresh">Bypasses the server-side query cache.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Overview, direct members, nested groups, effective members, topology, and parents.</returns>
    /// <remarks>
    /// The expensive call on these screens: it walks the nested-group graph under a server-side
    /// timeout and node, depth, edge, and result ceiling. It is never issued automatically — the
    /// operator asks for it — and a bounded result comes back as HTTP 200 with
    /// <c>isComplete=false</c>, which must not be presented as a complete answer.
    /// </remarks>
    public Task<DirectoryGroupAnalysisResponse> AnalyzeGroupAsync(
        string group,
        string? purpose,
        bool refresh,
        CancellationToken cancellationToken);

    /// <summary>
    /// Exports one group's membership as CSV.
    /// </summary>
    /// <param name="group">Exact group identifier.</param>
    /// <param name="mode"><c>DirectMembers</c> or <c>EffectiveMembers</c>.</param>
    /// <param name="purpose">Optional operational context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>File name, content type, and bytes exactly as the server produced them.</returns>
    /// <remarks>
    /// Requires <c>Identity.Groups.Export</c>. The server sorts deterministically, enforces a row
    /// ceiling, neutralizes spreadsheet formula characters, and refuses to export a partial
    /// effective-membership result. The UI generates no CSV of its own.
    /// </remarks>
    public Task<DirectoryExportFile> ExportGroupAsync(
        string group,
        string mode,
        string? purpose,
        CancellationToken cancellationToken);
}

/// <summary>One exported file exactly as the API produced it.</summary>
/// <param name="FileName">Server-supplied file name.</param>
/// <param name="ContentType">Server-supplied content type.</param>
/// <param name="Content">File bytes.</param>
public sealed record DirectoryExportFile(string FileName, string ContentType, byte[] Content);
