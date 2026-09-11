# InUseEvidence Standalone Operator Delivery

## Current Reporter Delivery, 2026-09-11

Use **operator-reporter-tr.md** for current A/B commands. Packaging now includes
candidate-dictionary.json and rfc-contract.template.json alongside the preserved
empty legacy dictionary. --inspect-candidates accepts only the two DOM-backed
direct candidates and cannot be combined with an RFC contract. A never traverses.
B uses --collect --rfc-contract after A establishes representation. An empty
template fails before transport. Candidates are not approved runtime mappings.

Observed SMSS_oRFF p_rel_requester means **Bildiren**. KEY display and SET user
reference remain separate. RequesterState is a compatibility name only;
ReporterState now describes the observed field, not an invented selector.
The deprecated ReporterProperty member may be omitted or equal p_rel_requester;
other selectors are rejected. Targets request only id/p_code/p_rel_requester.
The standalone private output also has LocalComparison: whitelisted actual
candidate/identity/reporter cells with matching aliases for local browser checks.
Never share that section. The returned Evidence and the HTTP diagnostic stay masked.
The independent Istem Sahibi selector remains unknown. Protected configuration,
transport, limits and privacy rules below still apply. Preserve old 6e05b45.

The following dated notes are historical discovery records, not current commands
or requirements for a separate Reporter selector. Runtime refresh is still
unwired pending operator evidence; this collector does not persist data.

## Two Missing Fields: Targeted Inspection, 2026-09-11

The runtime adapter requests the 15 legacy selects only. Its dictionary is NOT
runtime configuration: only this collector accepts it. Neither RFC nor Virtual
PC User was requested in the supplied 27-cell sample. Thus both are **NotQueried /
mapping not implemented**, not empty, forbidden, parse-rejected or lost in SQL.
The JSON aggregate and UI can retain/show named evidence, but runtime refresh
does not produce these fields or persist referenced-request owners yet.

The full legacy script and checked-in API evidence establish `query` + the
bounded `rel` filter (m_tid=100049, m_lid=selected OR). They do NOT establish a
single-service-item detail endpoint, field-metadata endpoint, wildcard projection,
or either missing selector. Do not guess such requests. The old `6e05b45` binary
has no RFC-hop option; do not pass new arguments to that package.

Smallest next operator action, in the existing authorized browser session:
1. Open ONE explicitly selected In Use OR, its Service Items relation, then ONE
   item whose membership is visible. Do not use Affected Assets as a substitute.
2. In browser developer tools, inspect the RFC control, then Virtual PC User.
   For each, collect ONLY the control's tag/type, id/name, label-for reference and
   relevant field/property data-attribute NAMES plus selector-like values. Use
   the control and its immediate label/wrapper only; no page/outerHTML export.
   Do not copy value attributes, hidden fields, event handlers, URLs, cookies,
   HAR, tokens or personal text. Replace generated record IDs consistently with
   `item-A` / `parent-A`. Record locally whether the visible value is empty.
3. Return those TWO sanitized fragments (max 2 KiB each). They are candidate
   property evidence, not proven API keys. If no selector-like attribute exists,
   stop and report that fact; do not enumerate the page or inventory.

After exact direct selectors are verified, the EXISTING bounded dictionary
collection (same protected JSON, login/session transport and `--collect`) can
query them with the selected parent OR's related items, at most ten. An empty
Virtual PC User does not prevent RFC collection. Compare actual visible RFC and
reference type locally; export only aliased keys/types/nesting and membership.
This task does not claim a newly packaged collector. A reviewed standalone build
of this source is required before using the changed requester-only RFC option.

For that option, omit `ReporterProperty` entirely when unverified. Three required
contract members remain: `RfcProperty` (same exact dictionary selector),
`ReferenceCellKind` (`SET` or `KEY`, evidenced), `ReferenceKind` (`SourceId` or
`OrCode`, evidenced). The target selects are then only `id`, `p_code`,
`p_rel_requester`; ReporterState=NotQueried. RequesterState distinguishes Returned,
Empty and Omitted. Exact id/code lookup includes closed requests, deduplicates
shared references and never recurses. Nothing here approves corporate ownership
semantics or changes the runtime refresh mapping.

## Optional Exact RFC Hop (Source Follow-up, 2026-09-10)

The preserved `6e05b45` standalone archive does NOT implement this new option.
The current collector source accepts `--collect --rfc-contract <absolute-json-path>`.
No corporate invocation or new release archive was made during implementation.
Without this option the existing legacy-only/two-field collection is unchanged.

Before using it, the source owner must provide these case-sensitive JSON members:

| Member | Required approved meaning |
|---|---|
| RfcProperty | Exact direct LCSIMS_ServiceInstance property; must equal dictionary.json's `RFC Kaydı` selector |
| ReferenceCellKind | `SET` or `KEY`, only as demonstrated by the response contract, not a name-based assumption |
| ReferenceKind | `SourceId` for canonical positive numeric SMSS_oRFF identity, or `OrCode` for exact OR-digits code |
| ReporterProperty | Optional; omit if unverified. When supplied, exact direct SMSS_oRFF Reporter selector, distinct from p_rel_requester |

No real values for these keys are currently verified. Do not use synthetic test
selectors as a corporate dictionary. Virtual PC User is optional and independent.
Reporter cell display/reference semantics remain evidence to inspect, not a
guessed KEY mapping or proof of technical creator/provisioning ownership.

After the dictionary/contract is approved, the same approved TEST management host,
private server-owned JSON configuration and normal session transport are used.
Append `--rfc-contract 'C:\OPERATOR_PRIVATE_CONFIG\approved-rfc-contract.json'`
to the existing five-argument command using a separately verified current tool
build and a new private output filename. Never use web.config as JSON.

The parent must be one exact active 4241/68 OR. Only its directly returned service
items can supply RFC references. Each distinct reference is queried once, serially,
with an exact id/code filter and only id/code/Requester/approved Reporter selects.
Referenced requests have NO active/category/group filter; closed requests are
allowed. No recursive request traversal, inventory/user enumeration or assignment.
Maximum 10 service rows and 10 distinct lookups, 45 seconds overall, 64 KiB per
response, 64 cells/row, three array levels. Existing session renewal allows one
transport retry per query. No unverified incremental source cursor is assumed.

Return only `Evidence`, including `ReferencedRequests.Links`: service-item alias,
reference alias, exact-match state and sanitized cells, or explicit missing,
ambiguous, denied/failed states. Equal aliases preserve links within this run;
they are not actual names or approved ownership. `CollectedNotMapped` is collection
success, not relationship resolution; inspect every link state. Stop on a nonzero
exit, missing/mismatched keys, ambiguous matches or denied reads; do not widen scope.
Keep ActorSid, both dictionary hashes, configuration and full output local. This
collector never persists WASAS owners or writes to Turuncu Hat/Jira/BPM.

This is an operator-invoked read-only console, not an API/UI deployment. Build on
the development machine with `scripts/release/New-InUseEvidencePackage.ps1`.
The delivery contains `tool/`, this guide, a blank configuration example,
`dictionary.json` containing `{}`, and source/runtime/hash metadata. Copy/extract
the whole delivery, not just the executable. Never put completed configuration
or collected evidence into the delivery directory, Git, or web-accessible paths.

## Host And Authentication

- Approved TEST Windows x64 API/management host, with installed x64 .NET 8
  `Microsoft.NETCore.App` AND `Microsoft.AspNetCore.App` shared runtimes. Read
  `dotnet --list-runtimes` and the delivered runtimeconfig/metadata. No SDK,
  repository, Office, SQL migration, IIS change or application installation.
  .NET 9/10 alone is not a substitute; use the organization's maintained 8.0 patch.
- The full `tool/` tree supplies application/NuGet dependencies. It includes
  Infrastructure's dependency closure but the collector does not resolve SQL,
  AD, Jira, API/UI hosts or worker services.
- The operator needs separately approved management execution, read access to
  protected source configuration, and create/write access to a private output
  directory. Windows SID is local attempt provenance, not WASAS OIDC delegation.
  The existing server-owned TuruncuHat login/session/query transport authenticates
  remotely. Neither UseDefaultCredentials nor a browser token is used.
- Existing approved HTTPS base route, DNS/network access and normal certificate
  trust must work. No redirects or TLS validation bypass. No new authentication
  values belong in a command line or in returned evidence.

## Accepted Configuration

The first argument is ONE absolute UTF-8 JSON file, loaded with AddJsonFile only.
No web.config/XML, IIS environment variables, `__` environment-key syntax,
appsettings overlay, user-secrets, or environment provider is loaded automatically.

Use the nested structure in `server-config.example.json`. Its empty/zero entries
are deliberately unusable. An authorized configuration owner must prepare the
effective values locally in a separately approved ACL-protected file, outside
the delivery. If values currently exist only in IIS environment configuration,
that approved local preparation step is REQUIRED; do not rename web.config to
JSON or modify IIS. Do not dump/export the whole IIS configuration. Transfer only
the two sections below through the owner's existing protected administration
process; no secrets need to be sent to the developer.

| Section | Required effective values |
|---|---|
| OperationalRecords | SourceProvider exactly `TuruncuHat`; ReadOnlyIntegrationMode `true`; ControlledTestWritesEnabled `false`; SourceCloseEnabled `false` |
| TuruncuHat | Existing HTTPS BaseUrl including its API path; Authorization, Username, Password (server-owned, never shared); TenantId greater than zero; verified SessionIdSegmentIndex and positive SessionLifetimeSeconds |
| TuruncuHat limits | Preserve reviewed ConnectTimeoutSeconds, RequestTimeoutSeconds, MaxResponseBytes, MaxDescriptionLength; example shows existing defaults 5/30/1048576/8000; DiagnosticContractLogging stays false |

No Jira, Access, Audit, OIDC, ConnectionStrings or BPM configuration is required.
SourceBaseObject/RelatedGroupId/ExcludedDccIds are not used for this diagnostic:
the exact In Use scope and legacy relation are fixed in the collector adapter.
The local output is its attempt record, not a SQL/WASAS audit entry. Do not widen
permissions or import unrelated settings to make a failed run succeed.

## One Initial Run

Verify the ZIP hash and every extracted file against the delivered manifests.
Keep `dictionary.json` unchanged as `{}`. In an approved interactive PowerShell
terminal replace the paths and `100` below (synthetic placeholder) with ONE
explicitly approved numeric source OR identity, not its OR code or WASAS GUID:

```powershell
& 'C:\OPERATOR_TOOL_DIRECTORY\tool\InUseEvidence.exe' 'C:\OPERATOR_PRIVATE_CONFIG\server-config.json' '100' 'C:\OPERATOR_TOOL_DIRECTORY\dictionary.json' 'C:\OPERATOR_PRIVATE_OUTPUT\inuse-one-or.json' --collect
```

All paths must be absolute; the private output parent must already exist, with
access restricted to the operator/integration owner. The output file must NOT
exist. Stop on nonzero `$LASTEXITCODE`, timeout or `StartedNotCompleted`; do not
retry with broader scope, change authentication, or return raw exception/config.
Success is exit 0 AND output `Status` exactly `CollectedNotMapped`.

Return ONLY the JSON `Evidence` object through the approved evidence channel:
`Root`, `ServiceItems` arrays of Path/Key/Type/Alias cells, `Completeness`,
`AffectedAssets`, `Mapping`. Keep ActorSid/At/DictionarySha256 and the full local
file private. Aliases preserve equal values across the one collection, not
approved business joins. `AffectedAssets=NotQueried` is not zero affected assets.

The first run queries five root selects to validate one active category 4241 /
group 68 OR, then the existing 15 legacy Service Item selects. It does NOT read
Virtual PC User or RFC, technical creator, or follow another request. Those fields
still need an approved property dictionary; screenshots do not establish keys.
Bounds: 45 seconds overall, one relation level, 10 related rows, 64 cells/row,
three array levels, 64 KiB per query response. Login uses the configured bounded
response limit. Session rejection allows one renewal/retry per query. Unsupported
key/value shapes, oversized/nonunique root or excess results stop without a
successful Evidence object. No uploads, approvals, source updates or BPM/Jira writes.
