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

Principal and member-list responses are bounded pages. `pageSize` defaults to 50 and cannot exceed 100. `continuationToken` is opaque, integrity-protected, target-bound, operation-bound, and expires after a configured interval. Clients repeat the same exact target with the token. Tokens contain no LDAP cookie or directory identifier. `refresh=true` is accepted only for a first page and bypasses the bounded cache.

`purpose` is optional on every read-only Directory Explorer request. Null, omitted, empty, and whitespace values mean no purpose. A supplied value is trimmed, limited by `DirectoryExplorer:MaxPurposeLength`, rejected when it contains control characters, and represented in audit only by its hash and length. This does not change future action/write workflows, which may require a change reason.

## Returned Data

Group summaries contain stable SID when available, name, sAMAccountName, distinguished name, description, `Security|Distribution`, and `Global|Universal|DomainLocal|Unknown`. Group detail may contain a managed-by distinguished-name identifier and direct member count only when the provider can obtain it safely.

Direct members contain stable SID when available, name, sAMAccountName, distinguished name, and `User|Group|Computer|Other`. No recursive data, profile enrichment, mail, phone, address, password metadata, SPNs, or raw attributes are returned.

## Security and Identity

`Identity.Groups.View` protects direct principal groups and group metadata. `Identity.Groups.Members.View` separately protects member enumeration. Authorization is derived from persisted application access and capabilities, never inline role/group-name checks. Admin receives both capabilities; Lead receives group visibility but not member enumeration.

Inputs are exact only. Wildcards, LDAP/filter syntax, raw distinguished names, bulk separators, control characters, and over-limit values are rejected before provider access. The configured backend provider performs reads under the approved runtime process identity through the same domain `PrincipalContext` model as exact identity lookup. SecureOps never collects an end-user AD username or password. Selecting the IIS runtime identity remains an infrastructure decision; this contract does not name or assume one.

## Reliability

Directory provider calls have configured timeouts and cancellation propagation. Results are deterministically ordered where AccountManagement supplies the complete bounded result. A configured provider result ceiling prevents unbounded server memory and work. Provider exceptions and timeout internals are converted to stable ProblemDetails codes and are never cached.

Conservative in-process caching is keyed by provider, normalized target, operation, offset, page size, and enrichment shape where applicable. TTL and entry count are bounded, and concurrent identical misses are coalesced to one provider call. Cache content is never an authorization source and `refresh=true` remains available to bypass it. Optional purpose text is excluded from cache and actor-and-operation rate-limit identities, so changing it cannot bypass protection.

## Audit

Each query writes append-only request/outcome evidence. Details contain operation, target hash/length, outcome, duration, provider-result counts, page size, continuation usage, and correlation metadata. Rate-limit rejection has a separate privacy-safe action and records that no provider call occurred. Full groups, members, names, DNs, descriptions, purpose text, and returned attributes are never copied into audit details. Required request audit fails closed before directory access.

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

Defaults are actor-and-operation fixed windows: principal-group/group-metadata queries allow 20 requests per 60 seconds, while direct-member queries allow 10 requests per 60 seconds. A rejected request returns the standard `RateLimitExceeded` ProblemDetails contract and does not invoke the directory provider. Changing optional purpose text does not create a new partition.

No new secret or credential key exists.

## Deferred

ADR-0015 and `docs/29-directory-explorer-phase2-enrichment.md` add bounded recursive/transitive membership, proven membership paths, account health, SPN/service evidence, and separately authorized privileged-group analysis through new routes. Phase 1 routes and direct-only semantics remain unchanged.

Computer lookup, manager/direct reports, directory writes, LDAP login, and corporate account classification remain deferred.
