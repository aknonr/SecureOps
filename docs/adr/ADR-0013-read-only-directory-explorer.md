# ADR-0013: Read-Only Directory Explorer

**Status:** Accepted for Phase 1 implementation
**Date:** 2026-08-23

## Context

ADR-0008 provides one exact user lookup for incident-response verification. Authorized operators also need bounded visibility into direct user groups, exact group metadata, and direct group members. This must not become people search, raw LDAP access, recursive expansion, or an alternative authentication flow.

## Decision

- Add a separate `IDirectoryGroupQueryService`; do not extend `IdentityController` or change the existing identity lookup contract.
- Expose POST query endpoints so account/group values and optional bounded operational purpose are carried in JSON bodies, not URLs. Missing or whitespace purpose means no purpose for these read-only operations.
- Use the selected identity provider: deterministic Fake locally and Active Directory through the existing process-identity `PrincipalContext` approach. No credentials are accepted or configured for browser users.
- Define explicit direct membership as one AD backlink/member edge. Principal groups use exact `memberOf` backlinks plus a separately labeled primary group derived from `primaryGroupID`; group members use `GroupPrincipal.Members` and explicitly do not claim primary-group completeness. `GetAuthorizationGroups()` and recursion are forbidden in Phase 1.
- Accept only exact normalized account/group inputs. No raw distinguished-name lookup, wildcard, partial, LDAP-filter, or arbitrary filter contract exists.
- Return bounded pages with a server-created opaque, integrity-protected, expiring continuation token. The token contains no account/group text and is not an LDAP paging cookie.
- Apply separate application capabilities: `Identity.Groups.View` for principal groups/group metadata and `Identity.Groups.Members.View` for direct member enumeration. Admin receives both; Lead receives metadata/group visibility only.
- Audit each privileged query with operation, actor, target hash/length, outcome, duration, page size, and result count. Never audit membership payloads.
- Use conservative bounded in-process caching and single-flight keyed by operation, normalized target, offset, page size, and provider. Refresh bypasses cache; exceptions are never cached. Optional purpose is excluded from cache and rate-limit identity.

## Consequences

- Existing exact sAMAccountName, PAM, UPN, normalization, cache, rate-limit, and ProblemDetails behavior is unchanged.
- Large groups are never returned unbounded to the browser. The AD provider applies a configured hard result limit; exceeding it fails with `DirectoryQueryLimitExceeded`.
- AccountManagement enumeration is bounded and timed out by the service, but cancellation cannot forcibly abort a native directory call already executing. Live AD smoke testing must verify representative large-group latency.
- Recursive/transitive membership, membership paths, privileged-group visibility rules, computers as first-class lookup targets, SPN enrichment, account health, and direct reports remain future work.

## References

- ADR-0008
- ADR-0010
- `docs/27-read-only-directory-explorer.md`
- `docs/05-security-model.md`
