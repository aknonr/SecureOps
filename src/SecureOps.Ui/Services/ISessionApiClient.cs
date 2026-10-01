using SecureOps.Shared.Contracts.OperationalRecords;
using SecureOps.Shared.Contracts.Sessions;

namespace SecureOps.Ui.Services;

/// <summary>
/// Client for the application-session and enterprise-integration diagnostics endpoints.
/// </summary>
/// <remarks>
/// The session contract deliberately exposes no cookie, IP address, device, or directory data — only
/// identifiers and timestamps. Nothing here should be extended to carry more: the opaque
/// <c>__Host-SecureOps.ApplicationSession</c> handle must never reach UI state, be logged, or be
/// rendered, and the responses are shaped so that it cannot.
/// </remarks>
public interface ISessionApiClient
{
    /// <summary>
    /// Reads the caller's own application session.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Safe timestamps, authentication method, and access version.</returns>
    /// <remarks>
    /// Fails with <c>SessionExpired</c> or <c>SessionRevoked</c> when the server has already ended
    /// the session — both are ordinary outcomes here, not errors to hide.
    /// </remarks>
    public Task<ApplicationSessionResponse> GetCurrentAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Reads one bounded page of active application sessions.
    /// </summary>
    /// <param name="page">One-based page number.</param>
    /// <param name="pageSize">Page size; the API caps it at the configured maximum.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Active sessions with safe metadata only.</returns>
    /// <remarks>Requires <c>Access.ManageUsers</c>.</remarks>
    public Task<ActiveApplicationSessionsResponse> GetActiveAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    /// <summary>
    /// Ends one application session.
    /// </summary>
    /// <param name="sessionId">Session to end.</param>
    /// <param name="reason">Justification recorded in the audit trail.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The terminal session identifier, end reason, and end time as the server recorded them.</returns>
    /// <remarks>
    /// Immediate and irreversible: the operator's next request is unauthenticated. It does not
    /// disable the account, and a later corporate authentication can establish a new session.
    /// </remarks>
    public Task<ApplicationSessionEndedResponse> RevokeAsync(
        Guid sessionId,
        string reason,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reads safe enterprise integration provider state.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Provider selection and status for Turuncu Hat and Jira.</returns>
    /// <remarks>
    /// Admin-only. The response carries provider name, configured selection, and a status word —
    /// never a URL, credential, identity, or remote payload.
    /// </remarks>
    public Task<EnterpriseIntegrationHealthResponse> GetIntegrationHealthAsync(
        CancellationToken cancellationToken);
}
