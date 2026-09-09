# InUseEvidence Standalone Operator Delivery

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
