# ADR-0015: Directory Explorer Phase 2 Read-Only Enrichment

**Status:** Accepted
**Date:** 2026-08-23

## Context

ADR-0013 exposes exact principal direct groups, exact group metadata, and bounded direct members. Authorized operations users also need explicit direct/transitive membership, provable membership paths, account-health evidence, bounded SPN visibility, and separately protected privileged-group analysis. These reads must not become raw LDAP access, an account-classification engine, or an authorization source.

## Decision

- Keep all Phase 1 routes and semantics unchanged. Phase 2 uses new POST contracts under `/api/v1/directory` so identifiers remain out of URLs.
- Extend the existing process-identity AccountManagement client architecture with provider-neutral enrichment primitives. Do not accept LDAP credentials, filters, distinguished names, wildcards, or partial search input.
- Model explicit direct membership as one `memberOf` edge, primary membership as a distinct `primaryGroupID` relationship, and transitive membership as groups reachable through two or more proven parent edges. A group reached both directly and transitively appears only in `directGroups`; `alsoTransitivelyReachable` records the additional evidence.
- Build a deterministic directed graph from exact directory relationships. Bound total depth, unique nodes, unique edges, total traversal time, and returned paths. Detect cycles and duplicate edges. Return explicit limit metadata instead of silently presenting a complete graph.
- Return a bounded deterministic set of shortest-first simple membership paths. A path is reported only when every edge was returned by the directory provider.
- Treat `lastLogonTimestamp` as approximate. Return nullable health fields when AD cannot prove a value. Derive `mustChangePassword` only from an available `pwdLastSet` value.
- Expose SPNs and account-type evidence without guessing that a naming pattern makes an account a service or PAM account. Zero SPNs is a successful empty read. Principal/SPN success is not invalidated by an independent membership-graph failure.
- Protect privileged-group analysis with `Identity.PrivilegedGroups.View`, assigned to Admin only. Monitored groups are exact server-owned identifiers; names are never hard-coded in source.
- Reuse the bounded Directory Explorer cache/single-flight and audit action family. Optional bounded purpose is not a cache or rate-limit key and is audited only by hash and length. Audit counts, duration, operation, outcome, and limit state, never membership graphs, SPNs, raw DNs, credentials, or personal profile payloads.
- Keep directory data live/provider-backed. Add no SQL migration; management reporting continues to aggregate existing Directory Explorer terminal audit actions.

## Consequences

- Native directory calls cannot always be interrupted after AccountManagement enters unmanaged code; the service still applies cancellation and a bounded total timeout.
- Limit metadata means a result can be valid but incomplete. Consumers must not treat cached or truncated directory evidence as an authorization decision.
- Future computer lookup can reuse exact principal evidence and graph primitives without changing the Phase 2 public contracts.

## References

- ADR-0008
- ADR-0010
- ADR-0013
- `docs/29-directory-explorer-phase2-enrichment.md`
- `docs/05-security-model.md`
