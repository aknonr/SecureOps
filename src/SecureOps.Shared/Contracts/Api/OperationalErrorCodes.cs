namespace SecureOps.Shared.Contracts.Api;

/// <summary>Stable operational error codes for RFC ProblemDetails responses.</summary>
public static class OperationalErrorCodes
{
    /// <summary>Identity input was invalid.</summary>
    public const string InvalidIdentityInput = "InvalidIdentityInput";
    /// <summary>An identity was not found.</summary>
    public const string IdentityNotFound = "IdentityNotFound";
    /// <summary>The caller lacks required access.</summary>
    public const string AccessDenied = "AccessDenied";
    /// <summary>A caller exceeded the permitted request rate.</summary>
    public const string RateLimitExceeded = "RateLimitExceeded";
    /// <summary>An identity provider is unavailable.</summary>
    public const string IdentityProviderUnavailable = "IdentityProviderUnavailable";
    /// <summary>An identity provider timed out.</summary>
    public const string IdentityProviderTimeout = "IdentityProviderTimeout";
    /// <summary>An identity provider returned an invalid response.</summary>
    public const string IdentityProviderBadResponse = "IdentityProviderBadResponse";
    /// <summary>An exact directory group was not found.</summary>
    public const string DirectoryGroupNotFound = "DirectoryGroupNotFound";
    /// <summary>An exact directory principal was not found.</summary>
    public const string DirectoryPrincipalNotFound = "DirectoryPrincipalNotFound";
    /// <summary>A Directory Explorer input was rejected.</summary>
    public const string DirectoryInvalidInput = "DirectoryInvalidInput";
    /// <summary>The configured directory provider was unavailable.</summary>
    public const string DirectoryProviderUnavailable = "DirectoryProviderUnavailable";
    /// <summary>A bounded directory query exceeded its server-side ceiling.</summary>
    public const string DirectoryQueryLimitExceeded = "DirectoryQueryLimitExceeded";
    /// <summary>The required audit store is unavailable.</summary>
    public const string AuditStoreUnavailable = "AuditStoreUnavailable";
    /// <summary>The authenticated identity is awaiting access approval.</summary>
    public const string AccessPending = "AccessPending";
    /// <summary>The authenticated user's application access is disabled.</summary>
    public const string AccessDisabled = "AccessDisabled";
    /// <summary>An access request decision is invalid for its current state.</summary>
    public const string AccessRequestInvalidState = "AccessRequestInvalidState";
    /// <summary>Access input failed deterministic validation.</summary>
    public const string AccessValidationFailed = "AccessValidationFailed";
    /// <summary>An access request already has a terminal decision.</summary>
    public const string AccessRequestAlreadyDecided = "AccessRequestAlreadyDecided";
    /// <summary>An access mutation used a stale version.</summary>
    public const string AccessConcurrencyConflict = "AccessConcurrencyConflict";
    /// <summary>An application user cannot accept the requested lifecycle transition.</summary>
    public const string AccessUserInvalidState = "AccessUserInvalidState";
    /// <summary>An access request or application user was not found.</summary>
    public const string AccessRecordNotFound = "AccessRecordNotFound";
    /// <summary>The request attempted self-approval.</summary>
    public const string AccessSelfApprovalDenied = "AccessSelfApprovalDenied";
    /// <summary>An idempotency key is invalid.</summary>
    public const string InvalidIdempotencyKey = "InvalidIdempotencyKey";
    /// <summary>Operational source authentication failed.</summary>
    public const string OperationalSourceAuthenticationFailed = "OperationalSourceAuthenticationFailed";
    /// <summary>Operational source is unavailable.</summary>
    public const string OperationalSourceUnavailable = "OperationalSourceUnavailable";
    /// <summary>Operational-record query failed.</summary>
    public const string OperationalRecordQueryFailed = "OperationalRecordQueryFailed";
    /// <summary>Operational record was not found.</summary>
    public const string OperationalRecordNotFound = "OperationalRecordNotFound";
    /// <summary>Operational record is in an invalid workflow state.</summary>
    public const string OperationalRecordInvalidState = "OperationalRecordInvalidState";
    /// <summary>Requester resolution failed.</summary>
    public const string RequesterResolutionFailed = "RequesterResolutionFailed";
    /// <summary>Requester resolution returned more than one exact match.</summary>
    public const string RequesterResolutionAmbiguous = "RequesterResolutionAmbiguous";
    /// <summary>Jira is unavailable.</summary>
    public const string JiraUnavailable = "JiraUnavailable";
    /// <summary>Jira rejected integration authorization.</summary>
    public const string JiraUnauthorized = "JiraUnauthorized";
    /// <summary>Jira rejected the proposed issue fields.</summary>
    public const string JiraValidationFailed = "JiraValidationFailed";
    /// <summary>Jira issue creation failed.</summary>
    public const string JiraCreateFailed = "JiraCreateFailed";
    /// <summary>A Jira issue is already persisted for the record and mapping.</summary>
    public const string JiraAlreadyCreated = "JiraAlreadyCreated";
    /// <summary>Operational-record close/update failed.</summary>
    public const string OperationalRecordCloseFailed = "OperationalRecordCloseFailed";
    /// <summary>No active source workflow activity matched the reviewed close contract.</summary>
    public const string OperationalRecordActivityNotFound = "OperationalRecordActivityNotFound";
    /// <summary>The source workflow activity query was ambiguous or invalid.</summary>
    public const string OperationalRecordActivityAmbiguous = "OperationalRecordActivityAmbiguous";
    /// <summary>Operational-record comment update failed.</summary>
    public const string OperationalRecordCommentUpdateFailed = "OperationalRecordCommentUpdateFailed";
    /// <summary>Another workflow operation owns the record.</summary>
    public const string WorkflowConflict = "WorkflowConflict";
    /// <summary>The workflow already completed.</summary>
    public const string WorkflowAlreadyCompleted = "WorkflowAlreadyCompleted";
    /// <summary>Another actor owns the active Operational Record lease.</summary>
    public const string OperationalRecordAlreadyClaimed = "OperationalRecordAlreadyClaimed";
    /// <summary>The external Operational Record changed after it was imported.</summary>
    public const string OperationalRecordChanged = "OperationalRecordChanged";
    /// <summary>The external Operational Record is no longer open.</summary>
    public const string OperationalRecordNoLongerOpen = "OperationalRecordNoLongerOpen";
    /// <summary>A command with the same scope is already executing.</summary>
    public const string WorkflowAlreadyInProgress = "WorkflowAlreadyInProgress";
    /// <summary>A reporting window or pagination request is invalid.</summary>
    public const string ReportingValidationFailed = "ReportingValidationFailed";
    /// <summary>The persistent reporting read model is unavailable.</summary>
    public const string ReportingUnavailable = "ReportingUnavailable";
}
