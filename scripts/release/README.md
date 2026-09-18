# API Release Packaging

`New-PairedTestRelease.ps1 -ReleaseName <reviewed-next-name>` requires a clean
committed feature branch (pre-existing `.vscode/` is excluded), publishes matching
API/UI/Worker on the combined delivery branch, reuses the payload scanners/validators,
exports the canonical Turkish runbook and packages the exact DBA 001-022 inventory
with per-file sizes/hashes and source metadata. Hangfire.SqlServer 1.8.6's original
schema-9 installation script is included separately for reviewed DBA provisioning;
runtime DDL stays disabled. Worker is a foreground console host, not a Windows Service.
It refuses an existing release directory. Determine the next name from actual
release metadata first. It does not deploy, activate writes or certify TEST acceptance.
Payload success is not release approval: `readyForInstallation` stays false;
the release owner records required gates in `evidence/validation.json`. DoD still
requires repository-wide format success; historical scoped passes are not a waiver.
Canonical runbook tokens bind the actual release name/build SHA. DBA 001-022
inclusion is a reference artifact, never an instruction to replay unchanged SQL.

For this already deployed rc6.22 with 001-021/Hangfire 9, `-UpgradeFromRc622`
exports `docs/inuse-v2-upgrade-tr.md` and emits only the additive 022 DBA delta.
The previous `-UpgradeFromRc621` no-delta switch is rejected by this source.
Original installed Branding can be retained; omit `-BrandingDirectory` when no
artwork changed. The effective-configuration comparator is included and hashed
under configuration/. None of these files overwrites server-owned settings.

`New-InUseEvidencePackage.ps1 -OutputDirectory <new-absolute-directory>` builds
only the standalone diagnostic on the development machine from committed HEAD.
It uses the shared payload/secret scanner, publishes framework-dependent win-x64,
smoke-checks the no-network usage path, creates per-file size/SHA256 and runtime/
source metadata, and verifies every ZIP entry. It refuses existing destinations;
it does not replace rc6.14, publish API/UI, deploy, or collect corporate evidence.
The target needs both .NET 8 shared runtimes, not an SDK or repository. Operator
instructions, the legacy `{}` dictionary, DOM candidate dictionary and unfilled
RFC representation template are in `scripts/diagnostics/InUseEvidence`. Current
A/B commands are in `operator-reporter-tr.md`; only Evidence may be shared.

`New-ApiDeploymentPackage.ps1` creates a path-preserving API ZIP and SHA256 payload manifest from a completed publish directory. It excludes controlled deployment configuration (`web.config` and `appsettings*.json`) and refuses to overwrite existing artifacts.

Before packaging, `Test-ApiReleasePayload.ps1` rejects PDB, source, project, test, log, `bin`, and `obj` payloads. It scans text files for credential-like assignments and can scan every publish file for caller-supplied ASCII and UTF-16 personal-path markers through `-ForbiddenText`.

`Validate-ApiAdRuntimeDependencies.ps1` then verifies that the manifest exactly represents the publish tree, ZIP paths and hashes match the manifest, required API/runtime files are present, API and Infrastructure assemblies share the expected dependency graph, and the complete `System.DirectoryServices.AccountManagement` runtime dependency closure is represented by the `.deps.json` and package assets.

These scripts do not publish, deploy, or modify server configuration. A package is not release-ready unless validation succeeds.

`New-UiDeploymentPackage.ps1` applies the same payload/secret scan and configuration
exclusions to a completed UI publish. It validates the UI runtime/dependency and
static-asset presence, preserves relative paths, verifies every ZIP entry hash,
writes a per-file SHA256 manifest, and refuses existing output files. It does not
claim API AD-runtime validation for UI binaries. API and UI packages for a paired
release must name the same exact build source SHA and required schema level in
the existing release-directory readiness manifest.

`New-UiDeploymentPackage.ps1 -Component Worker` reuses the same path-preserving
ZIP/hash/configuration exclusion logic for the existing console Worker. It checks
the Worker identity/runtime manifest, Hangfire/SQL dependencies and every declared
runtime/native/resource asset before packaging. The default remains Ui. Shared
scanning additionally rejects private key/certificate/database and fixture files.
No service installation or new hosting implementation is included.
