# Resource Catalogue

WorkspaceLayout is an optional bounded field in the same versioned personal JSON.
Legacy rows use defaults; owner-only layout saves/reset retain all membership and
use the existing transactional audit. Current capabilities filter shortcut reads
and gate saves. No schema/grant change. POST links/resolve revalidates at most 100
selected IDs without fetching destinations; see ADR-0019 for exact semantics.

Backend-owned local application data, not a destination-system integration.
ResourceCatalogueService revalidates persisted approved access and capabilities
on every operation. Resources.Manage is separate from general Lead authority.
Only internal UserId owns preferences; there is no arbitrary-owner API.

SqlResourceRepository follows Access:RepositoryProvider and uses SecureOpsDb.
Migration 010 after unchanged 001-009 creates categories, links and bounded JSON
personal aggregates. Every write compares a version under serializable locking
and inserts safe audit metadata in the same SQL transaction. InMemory is a
serialized test substitute, with audit failure preventing assignment.

Categories cap at 200, including archived rows. Personal preferences cap at
200 favourites, 20 sets and 100 distinct ordered links per set. A single personal
version guards changes to membership, ordering and the optional default. The
service rejects a mutation built from a snapshot whose version differs from the
client's expectation; SQL then rejects intervening writes. Reads never copy or
return unavailable/hidden link details from saved preferences.

ResourceValidation defines HTTPS/query/content policy without network I/O.
No HttpClient, browser, PowerShell, favicon or health-check integration exists.
Full replacement writes audit entry ID, version, archive/active state and actor
ID, not titles, URLs, set names, link selections or query values. No browsing
telemetry is added. Repeated accepted intents can create another version; stale
requests create no resource audit. Audit failure rolls back SQL state.

Personal-set PUT is a non-destructive merge: omitted IDs survive, including with
legacy requests and archive/restoration races. RemoveLinkIds explicitly removes
currently visible owned members. ResourceSetMembership preserves omitted ordered
slots; the service enforces capacity after merging. GuideDismissed is an optional
boolean in existing personal JSON, using the same version/audit transaction.
Environment lookup queries at most 101 distinct authorized values to return a
100-value page plus HasMore, with bounded search and no catalogue download.
No migration or new runtime grant is required for these corrections.

See ADR-0019, docs/contracts/secureops-api-v1-ui-integration.md and
docs/24-api-test-deployment-readiness.md for the contract, migration gates and
isolated SQL verification commands. No corporate validation is implied.
