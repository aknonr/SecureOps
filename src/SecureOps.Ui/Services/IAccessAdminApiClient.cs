using SecureOps.Shared.Contracts.Access;

namespace SecureOps.Ui.Services;

/// <summary>
/// Client for the access administration endpoints in
/// <c>docs/contracts/secureops-api-v1-ui-integration.md</c>.
/// </summary>
/// <remarks>
/// Every method here maps to exactly one endpoint that exists today. The contract offers no way to
/// list application users or to read another user's access record, so this interface deliberately
/// has no such method: an administrator reaches a user only through an access request. See G-8 in
/// <c>docs/26-ui-backend-contract-gaps.md</c>.
/// <para>
/// The three mutating calls all return authoritative server state, which callers must render instead
/// of assuming the change they requested is what landed.
/// </para>
/// </remarks>
public interface IAccessAdminApiClient
{
    /// <summary>
    /// Lists access requests, optionally filtered by decision status.
    /// </summary>
    /// <param name="status"><c>Pending</c>, <c>Approved</c>, <c>Rejected</c>, or <c>null</c> for all.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Matching access requests.</returns>
    public Task<IReadOnlyList<AccessRequestResponse>> GetRequestsAsync(
        string? status,
        CancellationToken cancellationToken);

    /// <summary>
    /// Approves a pending access request and assigns its initial roles.
    /// </summary>
    /// <param name="requestId">Request to approve.</param>
    /// <param name="reason">Justification recorded in the audit trail.</param>
    /// <param name="roles">Roles to grant; the API requires at least one.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The decided request as the server recorded it.</returns>
    public Task<AccessRequestResponse> ApproveAsync(
        Guid requestId,
        string reason,
        IReadOnlyList<string> roles,
        CancellationToken cancellationToken);

    /// <summary>
    /// Rejects a pending access request.
    /// </summary>
    /// <param name="requestId">Request to reject.</param>
    /// <param name="reason">Justification recorded in the audit trail.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The decided request as the server recorded it.</returns>
    public Task<AccessRequestResponse> RejectAsync(
        Guid requestId,
        string reason,
        CancellationToken cancellationToken);

    /// <summary>
    /// Replaces a user's active roles.
    /// </summary>
    /// <remarks>
    /// This is a replace, not a merge: roles omitted from <paramref name="roles"/> are removed.
    /// </remarks>
    /// <param name="userId">User whose roles change.</param>
    /// <param name="roles">The complete role set the user should end up with.</param>
    /// <param name="reason">Justification recorded in the audit trail.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The user's resulting access, including effective capabilities.</returns>
    public Task<CurrentAccessResponse> ReplaceRolesAsync(
        Guid userId,
        IReadOnlyList<string> roles,
        string reason,
        CancellationToken cancellationToken);

    /// <summary>
    /// Disables a user's application access.
    /// </summary>
    /// <param name="userId">User to disable.</param>
    /// <param name="reason">Justification recorded in the audit trail.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The user's resulting access, with no roles or capabilities.</returns>
    public Task<CurrentAccessResponse> DisableAsync(
        Guid userId,
        string reason,
        CancellationToken cancellationToken);
}
