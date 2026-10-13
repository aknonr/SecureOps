namespace SecureOps.Shared.Audit;

/// <summary>
/// Canonical audit action codes.
/// </summary>
public static class AuditActions
{
    /// <summary>An unsafe API request was rejected by the central request-intent guard.</summary>
    public const string ApiCsrfRejected = "ApiCsrfRejected";

    /// <summary>
    /// A privileged identity lookup was requested.
    /// </summary>
    public const string IdentityLookupRequested = "IdentityLookupRequested";

    /// <summary>
    /// A privileged identity lookup found a matching account.
    /// </summary>
    public const string IdentityLookupSucceeded = "IdentityLookupSucceeded";

    /// <summary>
    /// A privileged identity lookup found no matching account.
    /// </summary>
    public const string IdentityLookupNotFound = "IdentityLookupNotFound";

    /// <summary>
    /// A privileged identity lookup request was rejected before provider access.
    /// </summary>
    public const string IdentityLookupRejected = "IdentityLookupRejected";

    /// <summary>
    /// A privileged identity lookup failed before returning a trusted result.
    /// </summary>
    public const string IdentityLookupFailed = "IdentityLookupFailed";

    /// <summary>
    /// A privileged identity lookup timed out while waiting on the directory provider.
    /// </summary>
    public const string IdentityLookupProviderTimeout = "IdentityLookupProviderTimeout";

    /// <summary>
    /// Authorization denied access to the privileged identity lookup endpoint.
    /// </summary>
    public const string IdentityLookupForbidden = "IdentityLookupForbidden";

    /// <summary>A bounded bulk identity lookup was requested.</summary>
    public const string BulkIdentityLookupRequested = "BulkIdentityLookupRequested";

    /// <summary>A bounded bulk identity lookup completed.</summary>
    public const string BulkIdentityLookupCompleted = "BulkIdentityLookupCompleted";

    /// <summary>A privileged Directory Explorer query was requested.</summary>
    public const string DirectoryGroupQueryRequested = "DirectoryGroupQueryRequested";
    /// <summary>A privileged Directory Explorer query completed or returned not found.</summary>
    public const string DirectoryGroupQueryCompleted = "DirectoryGroupQueryCompleted";
    /// <summary>A Directory Explorer query was rejected before provider access.</summary>
    public const string DirectoryGroupQueryRejected = "DirectoryGroupQueryRejected";
    /// <summary>A Directory Explorer provider query failed safely.</summary>
    public const string DirectoryGroupQueryFailed = "DirectoryGroupQueryFailed";
    /// <summary>Authorization denied a Directory Explorer query.</summary>
    public const string DirectoryGroupQueryForbidden = "DirectoryGroupQueryForbidden";
    /// <summary>A Directory Explorer request was rejected by its actor-and-operation rate limit.</summary>
    public const string DirectoryGroupQueryRateLimited = "DirectoryGroupQueryRateLimited";
    /// <summary>A bounded group analysis reached a terminal outcome.</summary>
    public const string DirectoryGroupAnalysisCompleted = "DirectoryGroupAnalysisCompleted";
    /// <summary>An authorized bounded group membership export was produced.</summary>
    public const string DirectoryGroupMembershipExported = "DirectoryGroupMembershipExported";

    /// <summary>
    /// API or UI authorization denied a request.
    /// </summary>
    public const string AuthorizationDenied = "AuthorizationDenied";

    /// <summary>An operational record was imported or refreshed.</summary>
    public const string OperationalRecordImported = "OperationalRecordImported";
    /// <summary>An operational record received a deterministic classification outcome.</summary>
    public const string OperationalRecordClassified = "OperationalRecordClassified";
    /// <summary>A read-only Jira preview was generated.</summary>
    public const string JiraPreviewGenerated = "JiraPreviewGenerated";
    /// <summary>An authorized actor requested Jira creation.</summary>
    public const string JiraCreateRequested = "JiraCreateRequested";
    /// <summary>A Jira issue key was durably persisted.</summary>
    public const string JiraCreated = "JiraCreated";
    /// <summary>Jira creation failed safely.</summary>
    public const string JiraCreateFailed = "JiraCreateFailed";
    /// <summary>Source-record close/update was requested.</summary>
    public const string OperationalRecordCloseRequested = "OperationalRecordCloseRequested";
    /// <summary>The source record was closed/updated.</summary>
    public const string OperationalRecordClosed = "OperationalRecordClosed";
    /// <summary>Source-record close/update failed.</summary>
    public const string OperationalRecordCloseFailed = "OperationalRecordCloseFailed";
    /// <summary>An authorized actor requested workflow retry.</summary>
    public const string WorkflowRetried = "WorkflowRetried";
    /// <summary>The operational-record to Jira workflow completed.</summary>
    public const string WorkflowCompleted = "WorkflowCompleted";
    /// <summary>An authenticated corporate principal was first observed.</summary>
    public const string UserFirstSeen = "UserFirstSeen";
    /// <summary>A pending application access request was created.</summary>
    public const string AccessRequested = "AccessRequested";
    /// <summary>An authorized administrator viewed access requests.</summary>
    public const string AccessRequestsViewed = "AccessRequestsViewed";
    /// <summary>An authorized administrator viewed the access-user collection.</summary>
    public const string AccessUsersViewed = "AccessUsersViewed";
    /// <summary>An authorized administrator viewed one access-user record.</summary>
    public const string AccessUserViewed = "AccessUserViewed";
    /// <summary>An authorized administrator approved application access.</summary>
    public const string AccessApproved = "AccessApproved";
    /// <summary>An authorized administrator rejected application access.</summary>
    public const string AccessRejected = "AccessRejected";
    /// <summary>An authorized administrator disabled application access.</summary>
    public const string AccessDisabled = "AccessDisabled";
    /// <summary>An application role was assigned.</summary>
    public const string RoleAssigned = "RoleAssigned";
    /// <summary>An application role was removed.</summary>
    public const string RoleRemoved = "RoleRemoved";
    /// <summary>The one-time validated OIDC first-Admin grant was committed.</summary>
    public const string FirstAdminBootstrapped = "FirstAdminBootstrapped";
    /// <summary>An Operational Record workflow lease was acquired.</summary>
    public const string OperationalRecordClaimed = "OperationalRecordClaimed";
    /// <summary>An Operational Record workflow lease was released.</summary>
    public const string OperationalRecordClaimReleased = "OperationalRecordClaimReleased";
    /// <summary>An Operational Record workflow lease or state conflicted.</summary>
    public const string OperationalRecordConflict = "OperationalRecordConflict";
    /// <summary>The external Operational Record changed after import.</summary>
    public const string OperationalRecordSourceChanged = "OperationalRecordSourceChanged";
    /// <summary>An identity lookup used a short-lived cached result.</summary>
    public const string IdentityLookupCacheHit = "IdentityLookupCacheHit";
    /// <summary>An identity lookup called the configured directory provider.</summary>
    public const string IdentityLookupProviderCall = "IdentityLookupProviderCall";
    /// <summary>An authenticated caller requested provider-managed logout.</summary>
    public const string SessionLogoutRequested = "SessionLogoutRequested";
    /// <summary>A server-side SecureOps application session started.</summary>
    public const string ApplicationSessionStarted = "ApplicationSessionStarted";
    /// <summary>An application session reached its idle timeout.</summary>
    public const string ApplicationSessionIdleTimedOut = "ApplicationSessionIdleTimedOut";
    /// <summary>An application session reached its absolute timeout.</summary>
    public const string ApplicationSessionAbsoluteTimedOut = "ApplicationSessionAbsoluteTimedOut";
    /// <summary>A user ended their SecureOps application session.</summary>
    public const string ApplicationSessionLoggedOut = "ApplicationSessionLoggedOut";
    /// <summary>An administrator revoked an application session.</summary>
    public const string ApplicationSessionRevoked = "ApplicationSessionRevoked";
    /// <summary>Access disable ended active application sessions.</summary>
    public const string ApplicationSessionAccessDisabled = "ApplicationSessionAccessDisabled";
    /// <summary>An access-version change ended active application sessions.</summary>
    public const string ApplicationSessionAccessChanged = "ApplicationSessionAccessChanged";
    /// <summary>An administrator viewed active application sessions.</summary>
    public const string ApplicationSessionsViewed = "ApplicationSessionsViewed";
    /// <summary>A privileged management report was requested.</summary>
    public const string ManagementReportRequested = "ManagementReportRequested";
    /// <summary>A privileged management report was returned.</summary>
    public const string ManagementReportViewed = "ManagementReportViewed";
    /// <summary>A privileged management report could not be produced.</summary>
    public const string ManagementReportFailed = "ManagementReportFailed";
    /// <summary>An idempotent Jira create replay was stopped before external invocation.</summary>
    public const string JiraDuplicateCreatePrevented = "JiraDuplicateCreatePrevented";
}
