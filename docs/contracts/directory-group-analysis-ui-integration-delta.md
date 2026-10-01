# Directory and Identity UI Integration Delta

This is a backend contract delta for Claude-owned UI integration. It does not authorize UI changes by Codex.

## Identity Lookup

- `purpose` is optional on single and bulk read-only identity lookup. Omitted, null, empty, and whitespace are valid.
- Do not fabricate a default purpose.
- `alertId` and `turuncuhatEvtId` remain optional for wire compatibility but are deprecated for new read-only lookup clients. Do not render them in the normal Directory/Identity flow.

## Principal Evidence

- Render `membershipKind` as `Direct`, `Primary`, or `Transitive`; do not merge primary into ordinary direct membership.
- Treat zero SPNs as successful empty evidence.
- `membershipEvidenceAvailable=false` means SPN/principal evidence succeeded but group-count evidence is unavailable. Nullable group counts and traversal must not become a whole-tab directory failure.
- Never label an account Human, PAM, or Service Account from name, SPN count, or `passwordNeverExpires`.

## Group Analysis

Use `POST /api/v1/directory/groups/analysis` with `{ group, purpose?, refresh? }` after checking `Identity.Groups.Members.View`.

- Render group `name`, but use the additive server-returned `lookupKey` for subsequent group/member/analysis/path/export calls.
- `lookupKey` is an exact `sAMAccountName`; it is not a DN or LDAP filter. Never fall back to `distinguishedName`.
- Use the same rule for nested group navigation and user-to-group navigation. Member rows also expose nullable `lookupKey`.
- Direct-member pages already support 25/50/100 through `pageSize` and opaque `continuationToken`; do not add another paging route.

Render these as distinct views:

- `overview`
- `directMembers`
- `directNestedGroups`
- `effectiveMembers`
- `topologyNodes` plus `topologyEdges`
- `parentMemberships.directParents`
- `parentMemberships.transitiveParents`

`directMembers` follows explicit AD member-link semantics. `directMembersIncludePrimaryGroupMembers=false` is authoritative. Show incomplete state whenever `isComplete=false` or traversal metadata reports a limit. Do not turn partial evidence into an empty result.

## Export and Errors

Use `POST /api/v1/directory/groups/export` only when `Identity.Groups.Export` is present. Submit `mode=DirectMembers|EffectiveMembers` and `format=Csv`. The browser receives a file response. Do not relabel one mode as the other.

Handle stable outcomes independently: 404 not found, 400 invalid input, 403 forbidden, 429 rate limited, 503 `DirectoryProviderUnavailable`, 503 `DirectoryProviderTimeout`, 422 `DirectoryTraversalPartial`, and successful empty/partial 200 responses. Preserve the correlation ID for support.

Group overview state is independent from direct-member/analysis state. If member enumeration fails after overview succeeds, retain the overview and fail only that section. A successful empty member page is not an outage; an incomplete effective result must keep `isComplete=false` and traversal-limit evidence visible.
