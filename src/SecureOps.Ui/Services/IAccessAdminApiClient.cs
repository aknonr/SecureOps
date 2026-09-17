using SecureOps.Shared.Contracts.Access;

namespace SecureOps.Ui.Services;

/// <summary>
/// Client for the access administration endpoints in
/// <c>docs/contracts/secureops-api-v1-ui-integration.md</c>.
/// </summary>
/// <remarks>
/// Every mutation takes an <c>expectedVersion</c> and there is no overload without one. That is
/// deliberate: an optional version would let a caller omit it and silently overwrite a newer record,
/// which is exactly the failure optimistic concurrency exists to prevent. The version must come from
/// the read that populated the editor.
/// <para>
/// Note which version each mutation guards. Approve and reject check the <b>request's</b> version;
/// role replacement and disable check the <b>user's</b>. They advance independently, and passing one
/// where the other is expected produces a spurious conflict.
/// </para>
/// </remarks>
public interface IAccessAdminApiClient
{
    /// <summary>Reads a bounded server-side user page.</summary>
    public Task<AccessPage<AccessUserResponse>> PageUsersAsync(AccessPageQuery query, CancellationToken cancellationToken);
    /// <summary>Reads a bounded server-side request page.</summary>
    public Task<AccessPage<AccessRequestResponse>> PageRequestsAsync(AccessPageQuery query, CancellationToken cancellationToken);
    /// <summary>Reads persisted role definitions.</summary>
    public Task<IReadOnlyList<AccessRoleDefinition>> RolesAsync(CancellationToken cancellationToken);
    /// <summary>Reads implemented server-owned actions.</summary>
    public Task<IReadOnlyList<AccessActionDefinition>> ActionsAsync(CancellationToken cancellationToken);
    /// <summary>Computes a current effective-permission preview.</summary>
    public Task<AccessRoleImpact> PreviewRoleAsync(AccessRoleChange change, CancellationToken cancellationToken);
    /// <summary>Saves only a matching reviewed role impact.</summary>
    public Task<AccessRoleImpact> SaveRoleAsync(AccessRoleChange change, CancellationToken cancellationToken);
    /// <summary>
    /// Lists authoritative access-user records.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Every application user with status, roles, capabilities, profile, and version.</returns>
    /// <remarks>Requires <c>Access.ManageUsers</c>. The API applies no server-side filter.</remarks>
    public Task<IReadOnlyList<AccessUserResponse>> GetUsersAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Reads one authoritative access-user record.
    /// </summary>
    /// <param name="userId">User to read.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Status, assigned roles, effective capabilities, latest request, history, and version.</returns>
    /// <remarks>
    /// This is the read that must precede a role replacement: it supplies both the current role set
    /// and the version to submit with it.
    /// </remarks>
    public Task<AccessUserResponse> GetUserAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Lists access requests, optionally filtered by decision status.
    /// </summary>
    /// <param name="status"><c>Pending</c>, <c>Approved</c>, <c>Rejected</c>, or <c>null</c> for all.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Matching access requests.</returns>
    /// <remarks>Requires <c>Access.ApproveRequests</c>.</remarks>
    public Task<IReadOnlyList<AccessRequestResponse>> GetRequestsAsync(
        string? status,
        CancellationToken cancellationToken);

    /// <summary>
    /// Approves a pending access request and assigns its initial roles.
    /// </summary>
    /// <param name="requestId">Request to approve.</param>
    /// <param name="reason">Justification recorded in the audit trail.</param>
    /// <param name="roles">Roles to grant; the API requires at least one.</param>
    /// <param name="expectedVersion">The <b>request's</b> version from the read that populated the form.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The decided request as the server recorded it.</returns>
    /// <param name="roleVersions">Definitions reviewed in the permission preview.</param>
    public Task<AccessRequestResponse> ApproveAsync(
        Guid requestId,
        string reason,
        IReadOnlyList<string> roles,
        long expectedVersion,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, long>? roleVersions = null);

    /// <summary>
    /// Rejects a pending access request.
    /// </summary>
    /// <param name="requestId">Request to reject.</param>
    /// <param name="reason">Justification recorded in the audit trail.</param>
    /// <param name="expectedVersion">The <b>request's</b> version.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The decided request as the server recorded it.</returns>
    /// <remarks>
    /// Rejection is terminal. The user stays <c>Pending</c> but the API creates no replacement
    /// request, so this cannot be undone from the UI.
    /// </remarks>
    public Task<AccessRequestResponse> RejectAsync(
        Guid requestId,
        string reason,
        long expectedVersion,
        CancellationToken cancellationToken);

    /// <summary>
    /// Replaces a user's active roles.
    /// </summary>
    /// <remarks>
    /// A replace, not a merge: roles omitted from <paramref name="roles"/> are removed. Callers must
    /// seed the editor from <see cref="GetUserAsync"/> so the administrator edits the real set.
    /// </remarks>
    /// <param name="userId">User whose roles change.</param>
    /// <param name="roles">The complete role set the user should end up with.</param>
    /// <param name="expectedVersion">The <b>user's</b> version.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The user's resulting access, including effective capabilities.</returns>
    /// <param name="roleVersions">Definitions reviewed in the permission preview.</param>
    public Task<CurrentAccessResponse> ReplaceRolesAsync(
        Guid userId,
        IReadOnlyList<string> roles,
        long expectedVersion,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, long>? roleVersions = null);

    /// <summary>
    /// Disables a user's application access.
    /// </summary>
    /// <param name="userId">User to disable.</param>
    /// <param name="reason">Justification recorded in the audit trail.</param>
    /// <param name="expectedVersion">The <b>user's</b> version.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The user's resulting access, with no roles or capabilities.</returns>
    public Task<CurrentAccessResponse> DisableAsync(
        Guid userId,
        string reason,
        long expectedVersion,
        CancellationToken cancellationToken);
}
