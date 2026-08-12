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
    /// <summary>The required audit store is unavailable.</summary>
    public const string AuditStoreUnavailable = "AuditStoreUnavailable";
    /// <summary>The authenticated identity is awaiting access approval.</summary>
    public const string AccessPending = "AccessPending";
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
    /// <summary>Operational-record comment update failed.</summary>
    public const string OperationalRecordCommentUpdateFailed = "OperationalRecordCommentUpdateFailed";
    /// <summary>Another workflow operation owns the record.</summary>
    public const string WorkflowConflict = "WorkflowConflict";
    /// <summary>The workflow already completed.</summary>
    public const string WorkflowAlreadyCompleted = "WorkflowAlreadyCompleted";
}
