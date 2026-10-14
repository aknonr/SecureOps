# scripts/jea/

JEA endpoint packages for managed servers. **The product never registers, installs or copies them**: the server owner runs the
installer once per server, in an approved window. Repository data is synthetic; group and account names below are placeholders.

| Path | Purpose |
|---|---|
| `SecureOps.ServiceAccountUsage/` | Endpoint **1.0.0** (ADR-0024 R2): module (`.psd1`/`.psm1`), role capability `RoleCapabilities/SecureOpsServiceAccountUsage.psrc`, session configuration template `SecureOpsServiceAccountUsage.pssc`. One visible function `Get-SecureOpsAccountUsage` (Windows services, scheduled tasks, IIS identities — never passwords), `NoLanguage`, `RestrictedRemoteServer`, virtual run-as account, transcripts in `C:\ProgramData\SecureOps\JEA\Transcripts`. Output `service-account-usage-v1`; `-AsJson` returns it as one JSON text (used by the Worker, because remoting serializes nested objects shallowly). |
| `Install-SecureOpsServiceAccountUsageEndpoint.ps1` | Copies the module to `%ProgramFiles%\WindowsPowerShell\Modules\SecureOps.ServiceAccountUsage\<version>\`, writes the session file with `-ReadGroup` mapped to the only role, checks it and registers `SecureOpsServiceAccountUsage`. **Restarts WinRM.** Refuses the placeholder group, an existing registration or an existing version folder. `-WhatIf` shows the plan. |
| `Uninstall-SecureOpsServiceAccountUsageEndpoint.ps1` | Unregisters the endpoint (**restarts WinRM**) and removes the module folder (`-KeepModule` keeps it). Transcripts stay. |

Version: `ModuleVersion` in the `.psd1` is the endpoint version. A new version is installed after `Uninstall` (no in-place overwrite).
The output schema has no version field and no COM+ source yet; adding either is a contract change (schema + ADR-0027 parser).

## Server owner steps (once per target server)

1. AD team: the read gMSA (ADR-0024 R2-1, placeholder `CONTOSO\gmsa-so-sa-read$`) and the read group (placeholder
   `CONTOSO\SecureOps-SaRead`) with the gMSA as member; the Worker host may retrieve the gMSA password.
2. Copy this folder's `SecureOps.ServiceAccountUsage\` and the two scripts to the server (same relative layout), as an administrator:
   `.\Install-SecureOpsServiceAccountUsageEndpoint.ps1 -ReadGroup 'DOMAIN\SecureOps-SaRead' -WhatIf`, then without `-WhatIf`.
3. WinRM listener: the Worker uses HTTPS (5986) with Kerberos by default (`JeaRunner:UseSsl`); a server with only HTTP (5985) needs
   `JeaRunner:UseSsl=false` on the Worker (Kerberos still encrypts the payload) — decide per environment.
4. Check from an admin workstation (not the product): `Get-PSSessionConfiguration -Name SecureOpsServiceAccountUsage` on the server.

Tests (no server is contacted): `tests/SecureOps.Tests.Unit/ServiceAccounts/ServiceAccountUsageModuleTests.cs`,
`JeaEndpointPackageTests.cs`; the person-run collector `scripts/powershell/Get-ServiceAccountUsage.ps1` carries the module functions
verbatim (`ServiceAccountUsageScanScriptTests`).
