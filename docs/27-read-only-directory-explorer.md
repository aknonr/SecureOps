# Read-Only Directory Explorer

## Scope

Phase 1 adds three backend operations:

- exact user/PAM identity to direct groups;
- exact group to safe metadata;
- exact group to direct members.

Direct means one Active Directory membership edge. Nested groups are returned as `Group` members and are never expanded. Transitive authorization groups are not queried.

## API Contract

All operations use POST bodies so directory identifiers do not enter request URLs:

- `POST /api/v1/directory/principals/groups`
- `POST /api/v1/directory/groups/lookup`
- `POST /api/v1/directory/groups/members`

Principal and member-list responses are bounded pages. `pageSize` defaults to 50 and cannot exceed 100. `continuationToken` is opaque, integrity-protected, target-bound, operation-bound, and expires after a configured interval. Clients repeat the same exact target and purpose with the token. Tokens contain no LDAP cookie or directory identifier. `refresh=true` is accepted only for a first page and bypasses the bounded cache.

## Returned Data

Group summaries contain stable SID when available, name, sAMAccountName, distinguished name, description, `Security|Distribution`, and `Global|Universal|DomainLocal|Unknown`. Group detail may contain a managed-by distinguished-name identifier and direct member count only when the provider can obtain it safely.

Direct members contain stable SID when available, name, sAMAccountName, distinguished name, and `User|Group|Computer|Other`. No recursive data, profile enrichment, mail, phone, address, password metadata, SPNs, or raw attributes are returned.

## Security and Identity

`Identity.Groups.View` protects direct principal groups and group metadata. `Identity.Groups.Members.View` separately protects member enumeration. Authorization is derived from persisted application access and capabilities, never inline role/group-name checks. Admin receives both capabilities; Lead receives group visibility but not member enumeration.

Inputs are exact only. Wildcards, LDAP/filter syntax, raw distinguished names, bulk separators, control characters, and over-limit values are rejected before provider access. The AD provider uses exact AccountManagement identity operations and the IIS process/service identity through the same domain `PrincipalContext` model as exact identity lookup. SecureOps never collects an AD username or password.

## Reliability

Directory provider calls have configured timeouts and cancellation propagation. Results are deterministically ordered where AccountManagement supplies the complete bounded result. A configured provider result ceiling prevents unbounded server memory and work. Provider exceptions and timeout internals are converted to stable ProblemDetails codes and are never cached.

Conservative in-process caching is keyed by provider, normalized target, operation, offset, and page size. TTL and entry count are bounded. Cache content is never an authorization source and `refresh=true` bypasses it.

## Audit

Each query writes append-only request/outcome evidence. Details contain operation, target hash/length, outcome, duration, result count, page size, continuation usage, and correlation metadata. Full groups, members, names, DNs, descriptions, and returned attributes are never copied into audit details. Required request audit fails closed before directory access.

## Configuration

- `DirectoryExplorer__DefaultPageSize`
- `DirectoryExplorer__MaxPageSize`
- `DirectoryExplorer__ProviderResultLimit`
- `DirectoryExplorer__ProviderTimeoutSeconds`
- `DirectoryExplorer__ContinuationTokenLifetimeSeconds`
- `DirectoryExplorer__MaxGroupInputLength`
- `DirectoryExplorer__MaxPurposeLength`
- `DirectoryExplorer__Cache__Enabled`
- `DirectoryExplorer__Cache__TtlSeconds`
- `DirectoryExplorer__Cache__MaxEntries`
- `RateLimiting__DirectoryGroupQuery__PermitLimit`
- `RateLimiting__DirectoryGroupQuery__WindowSeconds`
- `RateLimiting__DirectoryGroupMembers__PermitLimit`
- `RateLimiting__DirectoryGroupMembers__WindowSeconds`

No new secret or credential key exists.

## Deferred

Recursive/transitive expansion, membership paths, privileged-group visibility policy, account health, SPN/service-account enrichment, computer lookup, manager/direct reports, directory writes, and LDAP login are not implemented.
