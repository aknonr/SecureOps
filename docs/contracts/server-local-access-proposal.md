# Server Local Access — Contract Proposal

**Status: Proposal — not accepted.** No code, route, DTO, capability or adapter exists for this. Nothing here may be built before the ADR in "ADR draft headings" is accepted by the owner (issue #80; owner decision 2026-10-17: no phase exception, documents only).

All examples are synthetic: company `CONTOSO`, server `srv-app-01.example`, identities `demo:*`. No real names, servers, addresses or screenshot content belong in this file (hard rule 10).

## Purpose

Operators want to type one server name and see who can administer it or log on to it remotely: the members of the server's **local Administrators** and **Remote Desktop Users** groups, one row per person, with the directory identity and the matching PAM account.

This is an **access inventory of a server**, not monitoring of people (hard rule 5).

## Where this sits in the roadmap

- Local-administrator inventory is Phase 5 and PAM correlation is Phase 4 (`docs/02-roadmap.md`). Hard rule 11 (stay in phase): this is surfaced, not built. The owner declined a phase exception on 2026-10-17.
- Resource-usage style discovery needs a separately approved read-only integration (`docs/31`). The remote read below is such an integration and needs the ADR.
- Existing building blocks that a later implementation would reuse, not change: `DirectoryMemberDto`, `DirectoryTraversalMetadataDto` (bounded traversal, `isComplete`; `docs/29`), `OperationalErrorCodes`, `AccessSnapshot.Can(...)`. There is no person→PAM mapping today; only `MockPamAccountResolver` exists.

## Proposed route

```
GET api/v1/directory/servers/{name}/local-access
```

Rules:

- **Read-only.** No write command is constructed anywhere in the path (hard rules 1 and 9).
- **Exact name.** `{name}` is one server name (FQDN or short, normalised server-side). No wildcards, no LDAP filter syntax, no lists, no `distinguishedName`. Anything else: `ServerLocalAccessInvalidInput` (400).
- **Remote read: JEA only** (hard rule 3). The Worker reads local group membership through a constrained endpoint with a read-only cmdlet allow-list (for example the local-group membership read cmdlets). Extending the allow-list needs the ADR. No unconstrained runspace, no ad-hoc remoting from the API process. Privileged access follows the decided model (direct WinRM + Kerberos + JEA as a read gMSA, ADR-0024 R2); BeyondTrust brokering is out of scope.
- **Server authorizes.** The UI hides nothing on its own authority; it reflects `AccessSnapshot.Can(<capability>)`.
- **Audited, fail closed.** See "Audit and fail-closed".

Only the two groups named below are queryable. The route takes no group parameter.

## Response shape (proposal)

```jsonc
{
  "server": { "name": "srv-app-01.example", "resolved": true },
  "readAt": "2026-10-17T09:30:00Z",
  "state": "Available",            // Available | Partial | NotConfigured | Unreachable | Unknown  (see "States")
  "groups": [
    {
      "group": "Administrators",   // closed set: Administrators | RemoteDesktopUsers
      "isComplete": true,
      "traversal": {               // same fields and meaning as DirectoryTraversalMetadataDto
        "nodesVisited": 12, "edgesVisited": 14, "maximumDepthReached": 2,
        "cycleDetected": false, "depthLimitReached": false, "nodeLimitReached": false,
        "edgeLimitReached": false, "providerResultLimitReached": false, "isTruncated": false
      },
      "members": [
        {
          "kind": "User",          // User | Group | UnresolvedSid
          "sid": "S-1-5-21-0-0-0-1001",          // synthetic placeholder
          "account": "CONTOSO\\demo:alice",        // as reported by the server; null when UnresolvedSid
          "person": {              // directory identity; null for Group / UnresolvedSid
            "displayName": "Demo Alice",
            "lookupKey": "demo:alice",
            "enabled": true
          },
          "pam": {
            "match": "Matched",    // Matched | NoMatch | NotAvailable  — never guessed
            "account": "demo:alice-adm",
            "source": "Directory attribute"       // how the link was proven; see open questions
          },
          "path": {
            "kind": "Direct",      // Direct | ViaGroup
            "viaGroups": []        // ordered group chain for ViaGroup, bounded
          }
        }
      ]
    }
  ],
  "limits": { "maxMembersPerGroup": 500, "maxNestingDepth": 5 }   // server-side ceilings, echoed for the UI
}
```

Semantics:

- **One row per person in the UI** is a presentation choice. The wire format is per (group, member); a person in both groups appears in both lists and the UI may merge them by `sid`. The server does not merge, so partial evidence cannot hide a membership.
- **`kind: Group`**: a nested group is returned as a member and expanded only inside the traversal limits. Expanded users carry `path.kind = ViaGroup` with the chain. A group that could not be expanded is listed with `isComplete=false` on the group.
- **`kind: UnresolvedSid`**: orphaned or unresolvable SIDs are shown as such. They are not dropped and not labelled "unknown user" by name guessing.
- **`person`** is the directory identity of exactly that SID. **Never** infer a person from an account name pattern.
- **`pam`**: `Matched` only when a proven link exists (source recorded). `NoMatch` is an explicit, reportable result ("no PAM account found for this person"). `NotAvailable` means the PAM correlation source is not configured or not reachable; that is different from `NoMatch`. Never guess from names, never label an account Human/PAM/Service Account from naming (consistent with the directory delta contract).
- **`isComplete`** is per group; the response `state` is `Partial` when any group is incomplete. A limit reached is never rendered as an empty or full list.
- No raw PowerShell output, no command text and no host credentials are ever returned.

## Error codes (proposed names)

Same style as `OperationalErrorCodes`: stable strings in RFC ProblemDetails. Existing codes are reused where they mean the same thing (`AccessDenied`, `RateLimitExceeded`, `AuditStoreUnavailable`, `DirectoryProviderUnavailable`, `DirectoryProviderTimeout`, `DirectoryTraversalPartial`).

| Proposed code | HTTP | Meaning |
|---|---|---|
| `ServerLocalAccessInvalidInput` | 400 | Name empty, wildcard/LDAP syntax, over length, not a single server name. |
| `ServerLocalAccessServerNotFound` | 404 | The exact server is not in the approved scope. Distinct from "unreachable". |
| `ServerLocalAccessNotConfigured` | 503 | The remote-read or PAM-correlation integration is not configured on this host. |
| `ServerLocalAccessUnreachable` | 503 | The server or its JEA endpoint did not answer (network, Kerberos, endpoint down). |
| `ServerLocalAccessTimeout` | 504 | The bounded read exceeded its time budget. |
| `ServerLocalAccessEndpointDenied` | 502 | The JEA endpoint refused the read (role/allow-list mismatch). Not a user permission problem. |
| `ServerLocalAccessBadResponse` | 502 | The endpoint answered with something the adapter cannot validate. |
| `ServerLocalAccessLimitExceeded` | 422 | A ceiling was hit and the caller asked for an operation that requires completeness (for example export). |
| `ServerLocalAccessPamUnavailable` | 200 + `pam.match=NotAvailable` | Not an HTTP error: PAM correlation failed, directory data is still valid. |

## States and honest UI display

The four states are distinct on screen and in text; colour is never the only signal. The UI states only what the server confirmed.

| State (TR) | Wire signal | What the UI shows | What it must not do |
|---|---|---|---|
| **Bağlı değil** (not configured) | `ServerLocalAccessNotConfigured`, or 404 without a code on a host that does not have the route | "Bu özellik sunucu tarafında henüz bağlı değil." Explains it is a platform state, not a result about the server. | Show an empty member table, or say the server has no administrators. |
| **Erişilemedi** (unreachable) | `ServerLocalAccessUnreachable`, `…Timeout`, `…EndpointDenied`, `…BadResponse`, `DirectoryProvider*` | "Sunucuya şu anda ulaşılamadı" with the stable error code and the time; a retry action. No stale data presented as current. | Claim the group is empty, show the last cached list as current, or say "healthy". |
| **Bilinmiyor** (unknown) | `state=Unknown`, or a member with `UnresolvedSid`, or `pam.match=NotAvailable` | Per item: "çözülemedi" / "PAM bilgisi yok (kaynak bağlı değil)". The row stays visible. | Replace unknown with "no match" or with a guessed name/account. |
| **Kısmi** (partial) | `isComplete=false` on any group, or `traversal.isTruncated`, or `DirectoryTraversalPartial` | A visible banner above the table: which group is incomplete and which limit was reached; the rows that exist are listed. Counts are labelled "en az". Export is disabled. | Show totals as final, or hide the banner when the list "looks fine". |

Also distinct from all four: **loading**, **empty with complete evidence** ("Bu grupta üye yok", only when `isComplete=true`), **denied** (403, `AccessDenied`) and **no match** (`pam.match=NoMatch`, a result, not an error).

## Capability proposal

Do **not** reuse `Identity.PrivilegedGroups.View` (`DirectoryPrivilegedGroupsView`): that is configured-group membership evidence for directory groups, and this route reads live local state from a managed server and links people to PAM accounts, which is a different sensitivity.

Proposed name: **`Servers.LocalAccess.View`** (constant `ServerLocalAccessView`).

Proposed holders (owner/Security decide): Security/Compliance reviewer roles and the administrator role; **not** granted by default to the general shift-operator role. Because the result links people to privileged accounts, it should not be implied by `Identity.Groups.Members.View`. A separate capability for any future export (`Servers.LocalAccess.Export`) is out of scope for the first cut.

## ADR draft headings

Headings only. No decision is stated or implied; each needs owner text and acceptance before any code.

1. **Faz istisnası / phase exception** — Phase 5 (local-admin inventory) and Phase 4 (PAM correlation) work before their phase; declined for now (2026-10-17). Open: re-evaluate at which phase, or split directory-only (no PAM) first.
2. **Kişi verisi ve gizlilik / person data and privacy** — rule 5: an access inventory of a server, not people monitoring. No per-person comparisons, no leaderboards, no "who has the most servers" view. Data minimisation (which person fields are returned), retention (none stored by default?), who may see it.
3. **Ters yön sorgu sınırları / reverse-lookup limits** — person → servers ("which servers can this person administer?"). Out of scope for this proposal. Needs its own limits (bounded server set, exact person only, no fleet scan, rate limit) and a stronger privacy review because it concentrates one person's access.
4. **PAM korelasyonu salt okunur / PAM correlation is read-only** — rule 7: BeyondTrust/PAM is not modified or brokered; correlation reads an approved source only; what proves a person↔PAM link; behaviour when the source is unavailable (`NotAvailable`, not `NoMatch`).
5. **Audit ve fail-closed / audit and fail closed** — see below for the draft requirements to be decided.
6. **JEA cmdlet allow-list extension** — the exact read-only cmdlets and endpoint role (rule 3 requires an ADR to extend).

## Audit and fail-closed

Draft requirements for the ADR to decide:

- Every successful and denied request writes an append-only audit record (rule 4): caller, capability checked, exact server name, outcome state, group completeness flags, time. **No member lists in the audit record.**
- If the audit store is unavailable, the request is refused with `AuditStoreUnavailable` (rule 6). If identity, authorization, configuration, the endpoint or the PAM source is missing or ambiguous, deny or return an explicit non-available state; never an optimistic result.
- Rate limiting per caller (`RateLimitExceeded`) and a hard cap on concurrent remote reads, so the feature cannot become a fleet scan or load the managed server.

## Open questions for owner / Security

1. Which phase, and is a directory-only first cut (no PAM, local groups only) acceptable earlier than PAM correlation?
2. How is a person proven to own a PAM account (directory attribute, naming convention, PAM export)? Naming convention alone is a guess and is not acceptable here; what is the approved read-only source?
3. Which roles hold `Servers.LocalAccess.View`? Is Security-only the right default?
4. Which servers are in scope (an approved inventory list, or any domain-joined name)? What happens for a name outside it (404 vs 403)?
5. Is the JEA endpoint for local-group read already part of an existing role capability, or does it need the allow-list ADR?
6. Is a person→servers reverse view wanted at all, given the rule 5 risk? If yes, under what limits?
7. Should member/person display include account-enabled state and last-logon? (Recommendation: no, not needed for an inventory and closer to monitoring.)
8. Retention: is nothing stored beyond the audit record, or may a point-in-time snapshot be kept for comparison of a server with itself over time?
9. Numbers: ADR number and the backend owner (Codex) for route/DTO/adapter; the UI starts only after ADR acceptance.

## What remains unverified

Everything: this is a text proposal. No integration, JEA role, directory attribute or PAM source was checked against the corporate environment, and none can be from a Mac or from public tooling.
