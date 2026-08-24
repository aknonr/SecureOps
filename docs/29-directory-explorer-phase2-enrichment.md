# Directory Explorer Phase 2 Read-Only Enrichment

## Scope

Phase 2 adds live, provider-backed operational evidence without changing Phase 1 routes:

- `POST /api/v1/directory/principals/memberships`
- `POST /api/v1/directory/principals/membership-paths`
- `POST /api/v1/directory/principals/account-health`
- `POST /api/v1/directory/principals/service-evidence`
- `POST /api/v1/directory/principals/privileged-memberships`

All requests require an exact account and allow an optional bounded operational purpose plus optional first-call cache refresh. Missing or whitespace purpose means no purpose; supplied text is trimmed and represented in audit only by hash and length. Membership-path requests additionally require one exact target group. Inputs never contain LDAP filters, raw distinguished names, wildcards, partial terms, credentials, or paging cookies.

## Membership Semantics

`directGroups` contains one-edge relationships and separately labels `membershipKind=Direct|Primary`. AD `memberOf` supplies direct backlinks but excludes the primary group, which is derived from `primaryGroupID`. `transitiveGroups` contains groups whose shortest discovered path has at least two edges. A group discovered both directly and through nesting is returned only in `directGroups`, with `alsoTransitivelyReachable=true`.

The provider supplies exact parent-group edges. The graph builder sorts every adjacency list, suppresses duplicate edges, detects cycles, and visits each unique group once for expansion. Traversal is bounded by depth, unique node count, unique edge count, provider result limits, cancellation, and a total timeout.

Membership paths are shortest-first simple paths over the proven graph. The result returns at most the configured path count and marks path truncation. `isMember=false` is returned only when the exact source and target exist but no path was proven within the reported traversal bounds. Consumers must inspect traversal metadata before interpreting a negative result as complete evidence.

## Account and Service Evidence

Account health exposes nullable `enabled`, `locked`, `passwordLastSetUtc`, calculated `passwordAgeDays`, `passwordNeverExpires`, `accountExpiresUtc`, `mustChangePassword`, and `lastLogonTimestampUtc`. `lastLogonTimestampUtc` is always labeled approximate and may be stale because AD replication is deliberately delayed. Missing attributes remain null; the API does not infer them.

Service evidence exposes bounded, sorted SPNs with total/truncation metadata, `managedBy`, account expiration, password age, nullable direct/transitive group counts, and AD object-class evidence. Zero SPNs is successful empty evidence. Membership graph failure does not discard successful principal/SPN evidence: `membershipEvidenceAvailable=false`, counts and traversal are null. An SPN, password setting, or naming pattern is evidence only; the API does not declare an account to be a corporate service or PAM account.

## Privileged Membership

`Identity.PrivilegedGroups.View` is distinct from general Directory Explorer access and is assigned only to Admin. `DirectoryExplorer:PrivilegedGroupIdentifiers` is an exact server-owned list of group SID, sAMAccountName, or name identifiers. Each configured group reports found/not-found, direct/transitive status, and bounded proven paths. Generic name text never implies privilege.

## Bounds and Configuration

| Key | Default |
|---|---:|
| `DirectoryExplorer__TraversalTimeoutSeconds` | `10` |
| `DirectoryExplorer__MaxTraversalDepth` | `8` |
| `DirectoryExplorer__MaxTraversalNodes` | `256` |
| `DirectoryExplorer__MaxTraversalEdges` | `512` |
| `DirectoryExplorer__MaxMembershipPaths` | `5` |
| `DirectoryExplorer__MaxSpnsPerPrincipal` | `50` |
| `DirectoryExplorer__MaxPrivilegedGroupIdentifiers` | `32` |
| `DirectoryExplorer__PrivilegedGroupIdentifiers__N` | empty/server-owned |
| `RateLimiting__DirectoryEnrichment__PermitLimit` | `6` |
| `RateLimiting__DirectoryEnrichment__WindowSeconds` | `60` |
| `RateLimiting__DirectoryPrivilegedGroups__PermitLimit` | `4` |
| `RateLimiting__DirectoryPrivilegedGroups__WindowSeconds` | `60` |

Existing provider timeout, exact-input, cache TTL/capacity, single-flight, and provider-result limits still apply. The 6-per-60-second enrichment and 4-per-60-second privileged-analysis defaults remain unchanged. Cache and rate-limit identities exclude optional purpose text. Startup rejects unsafe bounds, duplicate/invalid monitored identifiers, or a monitored list above its maximum.

## Audit and Reporting

Every request fails closed if required audit storage cannot accept the request event. Terminal audit metadata includes operation, outcome, duration, direct/transitive/result counts, traversed node/edge counts, and limit state. It excludes graph nodes, paths, SPNs, account-health values, raw DNs, credentials, and returned profile data.

Existing management reporting already classifies terminal `DirectoryGroupQuery*` actions as Directory Explorer adoption and security-quality evidence. No schema or migration change is required.

## Controlled Active Directory Validation

After an approved deployment, use non-sensitive exact test objects to verify one direct membership, one two-level nested membership, one non-member target, one account with no SPNs, one account with bounded SPNs, nullable health attributes, approximate `lastLogonTimestamp`, one configured privileged group, timeout behavior, and audit metadata. Verify all calls run as the approved IIS process identity and perform no directory write.
