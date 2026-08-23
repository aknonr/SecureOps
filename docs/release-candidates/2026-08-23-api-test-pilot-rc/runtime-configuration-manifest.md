# SecureOps API Pilot Runtime Configuration Manifest

## Rules

This is the authoritative Pilot profile for the 2026-08-23 release candidate. Values use IIS environment-variable syntax. Angle-bracket values are deployment inputs and must never be committed with real replacements. Server-owned `web.config`, `appsettings.json`, and `appsettings.*.json` are preserved and excluded from the runtime ZIP.

## Non-Secret Settings

| Key | Pilot value |
|---|---|
| `ASPNETCORE_ENVIRONMENT` | `Pilot` |
| `Swagger__Enabled` | `false` |
| `DemoAuth__Enabled` | `false` |
| `DemoAuth__HeaderName` | Do not set for Pilot; ignored while Demo authentication is disabled |
| `Access__DemoCompatibilityEnabled` | `false` |
| `Access__RepositoryProvider` | `SqlServer` |
| `Access__AutoCreateRequest` | `true` |
| `Access__OidcSubjectClaimType` | `sub`; reserved only, no OIDC handler is enabled |
| `Access__OidcIssuerClaimType` | `iss`; reserved only, no OIDC handler is enabled |
| `SessionSecurity__RepositoryProvider` | `SqlServer` |
| `SessionSecurity__IdleTimeoutMinutes` | `30` |
| `SessionSecurity__AbsoluteLifetimeHours` | `12` |
| `SessionSecurity__ActivityPersistenceIntervalMinutes` | `5` |
| `SessionSecurity__CookieName` | `__Host-SecureOps.ApplicationSession` |
| `SessionSecurity__MaxAdminPageSize` | `100` |
| `SessionSecurity__SecureCookie` | `true` |
| `SessionSecurity__HttpOnly` | `true` |
| `SessionSecurity__SameSite` | `Lax` |
| `SessionSecurity__RevalidateAccessOnEveryRequest` | `true` |
| `DataProtection__Mode` | `FileSystemDpapi` for the approved single-node Pilot |
| `DataProtection__ApplicationName` | `SecureOps.Api` |
| `Audit__Provider` | `SqlServer` |
| `Audit__FailClosed` | `true` |
| `Audit__RequirePersistentStoreInProduction` | `true` |
| `Audit__Queue__Enabled` | `true` |
| `Audit__Queue__Capacity` | `1000` |
| `Audit__Queue__FullBehavior` | `FailClosed` |
| `Audit__FlushIntervalSeconds` | `1` |
| `IdentityLookup__Provider` | `ActiveDirectory` |
| `IdentityLookup__StripDomainPrefix` | `true` |
| `IdentityLookup__NormalizeToLowerInvariant` | `true` |
| `IdentityLookup__EnableUpnLookup` | `true` |
| `IdentityLookup__MaxAccountLength` | `128` |
| `IdentityLookup__AllowedAccountPattern` | `^[a-zA-Z0-9._@-]+$` |
| `IdentityLookup__RegexTimeoutMilliseconds` | `250` |
| `IdentityLookup__ProviderTimeoutSeconds` | `3` |
| `IdentityLookup__BulkMaxAccounts` | `20` |
| `IdentityLookup__Cache__Enabled` | `true` |
| `IdentityLookup__Cache__TtlSeconds` | `30` |
| `IdentityLookup__Cache__MaxEntries` | `500` |
| `PamProvider__Provider` | `Mock`; no real PAM adapter exists |
| `PamProvider__TimeoutSeconds` | `3` |
| `DirectoryExplorer__DefaultPageSize` | `50` |
| `DirectoryExplorer__MaxPageSize` | `100` |
| `DirectoryExplorer__ProviderResultLimit` | `10000` |
| `DirectoryExplorer__ProviderTimeoutSeconds` | `5` |
| `DirectoryExplorer__ContinuationTokenLifetimeSeconds` | `300` |
| `DirectoryExplorer__MaxGroupInputLength` | `256` |
| `DirectoryExplorer__MaxPurposeLength` | `256` |
| `DirectoryExplorer__Cache__Enabled` | `true` |
| `DirectoryExplorer__Cache__TtlSeconds` | `15` |
| `DirectoryExplorer__Cache__MaxEntries` | `250` |
| `OperationalRecords__RepositoryProvider` | `SqlServer` |
| `OperationalRecords__SourceProvider` | `Disabled` until the separate real-integration gate is approved |
| `OperationalRecords__MaxImportCount` | `100` |
| `OperationalRecords__ClaimLeaseSeconds` | `120` |
| `Jira__Provider` | `Disabled` until the separate real-integration gate is approved |
| `Jira__AuthenticationMode` | `Basic` when `Corporate` is approved |
| `Jira__IssueType` | `Task` |
| `Jira__UnresolvedRequesterPolicy` | `Block` |
| `Jira__SummarySeparator` | `: ` |
| `Jira__SummaryMaxLength` | `255` |
| `Jira__AssignmentMode` | `ProjectDefault` |
| `Jira__ReporterMode` | `ProjectDefault` |
| `Jira__ConnectTimeoutSeconds` | `5` |
| `Jira__RequestTimeoutSeconds` | `30` |
| `Jira__MaxResponseBytes` | `1048576` |
| `Jira__UserSearchMaxAttempts` | `3` |
| `Jira__UserSearchRetryDelayMilliseconds` | `500` |
| `TuruncuHat__SessionIdSegmentIndex` | `1` |
| `TuruncuHat__ConnectTimeoutSeconds` | `5` |
| `TuruncuHat__RequestTimeoutSeconds` | `30` |
| `TuruncuHat__MaxResponseBytes` | `1048576` |
| `TuruncuHat__MaxDescriptionLength` | `8000` |
| `CommandIdempotency__ExecutionLeaseSeconds` | `120` |
| `CommandIdempotency__MaxKeyLength` | `128` |
| `RateLimiting__IdentityLookup__PermitLimit` | `10` |
| `RateLimiting__IdentityLookup__WindowSeconds` | `60` |
| `RateLimiting__BulkIdentityLookup__PermitLimit` | `4` |
| `RateLimiting__BulkIdentityLookup__WindowSeconds` | `60` |
| `RateLimiting__DirectoryGroupQuery__PermitLimit` | `20` |
| `RateLimiting__DirectoryGroupQuery__WindowSeconds` | `60` |
| `RateLimiting__DirectoryGroupMembers__PermitLimit` | `10` |
| `RateLimiting__DirectoryGroupMembers__WindowSeconds` | `60` |
| `RateLimiting__OperationalRecordRefresh__PermitLimit` | `12` |
| `RateLimiting__OperationalRecordRefresh__WindowSeconds` | `60` |
| `RateLimiting__JiraPreview__PermitLimit` | `20` |
| `RateLimiting__JiraPreview__WindowSeconds` | `60` |
| `RateLimiting__JiraCreate__PermitLimit` | `6` |
| `RateLimiting__JiraCreate__WindowSeconds` | `60` |
| `RateLimiting__WorkflowRetry__PermitLimit` | `6` |
| `RateLimiting__WorkflowRetry__WindowSeconds` | `60` |
| `ReverseProxy__ForwardedHeaders__Enabled` | `false` until exact proxy source/header behavior is approved |

Management reporting has no provider key. It uses SQL automatically when both `Access__RepositoryProvider` and `OperationalRecords__RepositoryProvider` are `SqlServer`.

## Secret or Runtime-Only Settings

| Key | Controlled input |
|---|---|
| `ConnectionStrings__SecureOpsDb` | `Server=tcp:<SQL_FQDN>,<SQL_PORT>;Database=<DATABASE_NAME>;Integrated Security=True;Encrypt=True;TrustServerCertificate=False;Application Name=SecureOps.Api;Connect Timeout=15` |
| `DataProtection__KeyRingPath` | `<ABSOLUTE_SERVER_OWNED_KEY_RING_PATH_OUTSIDE_DEPLOYMENT>` |
| `DataProtection__CertificateThumbprint` | Omit for `FileSystemDpapi`; required only for approved `FileSystemCertificate` multi-node mode |
| `IdentityLookup__DomainName` | `<APPROVED_AD_DNS_DOMAIN>` |
| `IdentityLookup__Container` | Optional `<APPROVED_CONTAINER_DN>` |
| `Access__BootstrapAdministrators__0` | `<EXACT_APPROVED_WINDOWS_PRINCIPAL>`; remove after at least two persisted reviewed Admins exist |
| `ReverseProxy__ForwardedHeaders__TrustedProxyIps__N` | Omit while forwarding is disabled; otherwise exact approved proxy IPs only |
| `TuruncuHat__BaseUrl` | `<APPROVED_HTTPS_BASE_URL>` |
| `TuruncuHat__Authorization` | `<SECRET_RUNTIME_AUTHORIZATION_VALUE>` |
| `TuruncuHat__Username` | `<SECRET_RUNTIME_USERNAME>` |
| `TuruncuHat__Password` | `<SECRET_RUNTIME_PASSWORD>` |
| `TuruncuHat__TenantId` | `<APPROVED_TENANT_ID>` |
| `TuruncuHat__SourceBaseObject` | `<APPROVED_SOURCE_BASE_OBJECT>` |
| `TuruncuHat__RelatedGroupId` | `<APPROVED_RELATED_GROUP_ID>` |
| `TuruncuHat__ExcludedDccIds__N` | `<APPROVED_EXCLUDED_DCC_ID>` |
| `TuruncuHat__ActivityBaseObject` | `<APPROVED_ACTIVITY_BASE_OBJECT>` |
| `TuruncuHat__ActivityTaskModelId` | `<APPROVED_ACTIVITY_TASK_MODEL_ID>` |
| `TuruncuHat__ActivityGroupId` | `<APPROVED_ACTIVITY_GROUP_ID>` |
| `TuruncuHat__ActivityMainObjectTypeId` | `<APPROVED_ACTIVITY_MAIN_OBJECT_TYPE_ID>` |
| `TuruncuHat__CompletedStatusId` | `<APPROVED_COMPLETED_STATUS_ID>` |
| `TuruncuHat__CompletionCommentTemplate` | `<APPROVED_TEMPLATE_CONTAINING_{JiraKey}>` |
| `TuruncuHat__SessionLifetimeSeconds` | `<APPROVED_CONSERVATIVE_SECONDS>` after expiry semantics are evidenced |
| `Jira__BaseUrl` | `<APPROVED_HTTPS_BASE_URL>` |
| `Jira__Authorization` | `<SECRET_RUNTIME_BASIC_AUTHORIZATION_VALUE>` |
| `Jira__ProjectKey` | `<APPROVED_PROJECT_KEY>` |
| `Jira__IssueTypeId` | `<APPROVED_TASK_TYPE_ID>` |
| `Jira__MappingVersion` | `<REVIEWED_MAPPING_VERSION>` |
| `Jira__TeamCustomField` | `<APPROVED_TEAM_FIELD_KEY>` |
| `Jira__TeamValue` | `<APPROVED_TEAM_VALUE>` |
| `Jira__RequesterWatcherCustomField` | `<APPROVED_WATCHER_FIELD_KEY>` |
| `Jira__Labels__N` | `<APPROVED_LABEL>` |
| `Jira__OperatorAssigneeMappings__N__SecureOpsActor` | Omit for `ProjectDefault`; otherwise exact deployment-verified SecureOps actor |
| `Jira__OperatorAssigneeMappings__N__JiraUsername` | Omit for `ProjectDefault`; otherwise exact deployment-verified Jira username |

## Authentication Assumptions

- Production-style authentication is IIS Windows Authentication with ASP.NET Core Negotiate; no LDAP password form, SQL login, bearer token, or Demo header is used.
- IIS must run the API under `DOMAIN\WASAST_YONETIM`, enable Windows Authentication, and disable Anonymous Authentication before Pilot validation.
- The SecureOps application-session cookie is not the corporate authentication source.
- Integrated SQL authentication, SPNs, SQL TLS trust, and IIS identity are external deployment gates, not code settings.
