# ADR-0025: Bounded directory name search for Service Accounts

**Status:** Accepted by the project owner as a functional extension (2026-10-01). **Corporate security review and
production deployment approval are not claimed**; the feature ships behind the existing module and identity-lookup
gates and is validated on Windows by the platform owner (Codex) before any live use.
**Date:** 2026-10-01
**Decision makers:** project owner (functional scope). Bilgi Güvenliği review is still required before production.
**Amends:** ADR-0008 ("Broad wildcard search" stays rejected; this ADR allows one bounded, prefix-only form).

## Context

### Owner Amendment, 2026-10-02

Expose the same bounded first/full-name query on the general AD lookup page,
alongside exact-account lookup, through `POST /api/v1/identity/name-search`.
This route requires only the approved persisted `Identity.Lookup` capability,
the existing IdentityLookup rate limit and fail-closed, name-free audit. It does
not depend on Service Accounts activation, View or data scope, and returns no
inventory ids or links. The existing module route retains both capability gates
and links only uniquely matching records within explicit Service Accounts scope.
The general UI may select a returned exact account; that selection grants nothing.
All query bounds, Turkish/multipart matching, LDAP escaping, provider limits and
minimal fields below remain unchanged. This amendment supersedes the rejection of
the general page placement, not the rejection of broad people/directory browsing.
Corporate AD reads and target installation are not authorized by this source task.

Service account coordinators often know a person by name ("Ayşe Yılmaz, altyapı") but not the account name, and must
leave the tool to find it before they can use exact lookup (ADR-0008) or open the account record. ADR-0008 rejected a
broad wildcard search because it would turn the platform into a people-browsing tool.

## Decision

- Add `POST /api/v1/service-accounts/directory/name-search` with body `{ "query": "..." }`. The query is never in the
  URL. Exact lookup `POST /api/v1/identity/lookup` is unchanged.
- **Who:** the caller needs the Service Accounts module View capability **and** the platform capability
  `Identity.Lookup` (policy `CanIdentityLookup`). The same `IdentityLookup` rate limit applies.
- **Input:** first name or full name, 3–64 characters, at least 3 letters, at most 4 words; only letters, combining
  marks, space, apostrophe, hyphen and period. Wildcards and LDAP filter characters are rejected before any provider
  call and escaped again (RFC 4515) when the filter is built. Only a trailing wildcard is added by the server.
- **Turkish names:** the filter and result check use the same Turkish/ASCII equivalence (İ/ı/I/i, ş/s, ğ/g,
  ç/c, ö/o, ü/u). All given-name words before the last surname token must match; middle words are never dropped.
  Spelling expansion is capped at 256 alternatives; an excessive query returns `NameQueryTooComplex` before
  provider access. Each alternative remains a prefix, never a substring or caller-provided wildcard.
- **Output:** at most 10 results, with only display name, account name and department, plus a `SameNameAsAnother`
  flag so same-named people can be told apart, and a `Truncated` flag that tells the caller to narrow the query.
  No e-mail, phone, manager, group, SID or distinguished name.
- **Inventory links:** a result carries a Service Accounts record id only when exactly one record with that account
  name is inside the caller's module scope. Out-of-scope or ambiguous records are not revealed, not even by count.
- **No effect:** choosing a result grants no permission, does not confirm account ownership and never changes the
  directory. The provider binds with the process identity, reads five attributes, uses size and time limits and does
  not chase referrals.
- **Audit:** `ServiceAccount.DirectoryNameSearchRequested` is written before the provider is called (audit failure
  stops the search), then `...Completed` with counts or `...Failed` with `NotConfigured`/`Timeout`/
  `ProviderUnavailable`. The audit holds a salted query hash, its length and word count, never the name.
- **Unavailable provider:** 503 `ServiceAccountDirectoryUnavailable`; no partial or cached answer.

## Consequences

- Personal-data exposure grows slightly (a name now resolves to up to 10 accounts). The narrow field set, the
  double capability gate, the rate limit and the name-free audit are the mitigations a security reviewer should check.
- The Active Directory provider (`ActiveDirectoryNameSearchProvider`) is exercised on Windows only; Linux tests use the
  synthetic `MockDirectoryNameSearchProvider` and synthetic `syn.*` accounts.
- No SQL schema change: linking reads the existing `svcacct.Accounts` table with the module scope predicate.

## Rejected alternatives

- **Substring/contains search:** browses the directory; prefix-only is enough for names.
- **Returning more attributes (e-mail, title, manager):** not needed to tell results apart.
- **Platform-wide people search page:** outside the Service Accounts purpose; exact lookup remains the platform tool.

## References

- ADR-0008 (read-only identity lookup), `docs/service-accounts/SPEC.md`, `docs/service-accounts/WINDOWS-ACCEPTANCE.md`
