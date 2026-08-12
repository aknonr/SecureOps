namespace SecureOps.Shared.Audit;

/// <summary>
/// Canonical audit action codes.
/// </summary>
public static class AuditActions
{
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
}
