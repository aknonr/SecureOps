# API Release Packaging

## Admin / Lookup 027 API/UI Review

For this correction, `New-ServiceAccountsTestReview.ps1 -FromVerifiedMaster
-ApiUiOnly -UpgradeFromInstalled026 -TestedProductSource <verified-master-SHA>
-SqlUpgradeReview <private-source-bound-review.json> -OutputDirectory <new-directory>`
retains exact/clean master, source identity, dependency/payload/hash and destination
guards. It exports only migration/schema 027 plus existing API grant references;
installed 024-026 are never replayed. No Worker is built or packaged. The review
uses `wasas.sql-upgrade-review.v1`, the exact source, typed-true
`Baseline026Verified` / `AdminNavigation027Reviewed`, nonempty
`Baseline026EvidenceReference` / `AdminNavigation027ReviewReference`, and exact
hashes for the two 027 files plus SA-API-permissions and SA-002-API-permissions.
Baseline evidence can be isolated local proof for a NOT-installation-approved
candidate; it never asserts target SQL was observed/applied. Legacy selection
still refuses a 027 inventory. Missing, false, stale or changed inputs fail closed.
The candidate includes the reviewed recovery fragment (no full runtime config)
and one current operator checklist. `readyForInstallation` remains false.

## Combined 026 Review Candidate

The current SQL closure is 001-026. Explicit Service Accounts selection requires source-bound
`ServiceAccounts026Reviewed=true`, `ServiceAccounts026ReviewReference`, and all eleven 024/025/026
DDL-wrapper/include/grant hashes. It exports 024-026 and three unassigned role scripts; installed
schemas are not replayed. Existing release branch, clean-source and product guards remain.
`New-ServiceAccountsTestReview.ps1 -FromVerifiedMaster -TestedProductSource <remote-master-SHA>
-SqlUpgradeReview <private-review.json> -OutputDirectory <new-directory>` additionally requires
clean local master equal to origin/master and the verified source. It prepares a matched review
candidate, never installation approval. Previous 025-only descriptions below are historical.

`New-ServiceAccountsTestReview.ps1` prepares a separate matched review candidate,
NOT a numbered successor. It requires the clean pinned integration branch and
product-input equality to the supplied tested source. Entry versions retain that
tested source; candidate metadata separately identifies SQL/docs preparation.
It reuses the existing payload scanners and ZIP/dependency validators. Only the
024/025 SQL dependency closure and unassigned role scripts are exported; no
target configuration or private/local test evidence is packaged. The numbered
release guard still requires the combined branch and clean exact source. Its
explicit 025 selection is described below; an existing review ZIP is never
promoted or relabelled by this change. No installation readiness is claimed.

## Reviewed 023 -> 024 -> 025 Selection

`New-PairedTestRelease.ps1` now requires `-ExpectedSource <full reviewed HEAD>`.
The branch remains exactly `feature/combined-test-delivery-20260915`, clean-tree
and assembly source checks remain enforced, and failures occur before publishing.
Do not rename an active/dirty worktree to evade the branch guard. A later authorized
promotion can use an independent clone of the complete reviewed local source,
then create that branch at the exact approved commit. Preserve every original
worktree; do not merge master, push or recreate packages as part of preparation.

025 is opt-in ONLY with `-UpgradeFromRc626 -IncludeServiceAccounts
-SqlUpgradeReview <private review.json>`. `Get-ReleaseSqlPlan.ps1` is read-only:
it validates exact 001-025 inventories, selects only 024/025 (not installed
022/023), verifies the SQLCMD include dependency and exports role scripts
separately. Without this opt-in, an inventory containing 025 fails closed.
The private review has schema `wasas.sql-upgrade-review.v1`, `Source` equal to
ExpectedSource, boolean `Baseline023Verified`, `Delta024Reviewed` and
`ServiceAccounts025Reviewed` all true, and nonempty `BaselineEvidenceReference`,
`Delta024ReviewReference`, `ServiceAccounts025ReviewReference`. `Files` contains
exact Path/Sha256 entries for the two 024 files, two 025 files, included SA-001
DDL and both role scripts (seven files). No SQL connection or credentials belong
in this record. Typed false/missing values, changed hashes and source drift refuse
selection. This is evidence-bound packaging selection, NOT target execution
authorization, live permissions verification or proof of applied 024/025.

SQLCMD working directory is the delivered `sql/migrations`; nested `:r` resolves
from there. Keep `sql/schema/025-service-accounts.sql` AND
`sql/pending/service-accounts/SA-001-service-accounts.sql`. Execute 024 using
`-I -b`, verify its reviewed contract and STOP on differences before separately
approved 025. Role scripts create roles without members; their execution and
principal membership need their own change approval. Full 001-025 is reference
material, not an upgrade command. Backup/stop/recovery gates remain in the
current operator entry; readyForInstallation remains false.

Focused Windows selector/include/failure coverage (no packaging):
`tests/release/Test-PairedReleaseSqlSelection.ps1 -EvidenceDirectory <new private
directory> -VerifySqlCmd -DatabaseSuffix <new local suffix>`. It uses ONLY the
hard-coded per-user LocalDB instance and refuses existing databases. Earlier
001-024 descriptions below are historical for pre-025 product sources.

Historical pre-025 continuation required schema 001-024. Use `-UpgradeFromRc626`
only with verified installed 023 for a 024-only delta. `-UpgradeFromRc624`
requires verified 022 for a 023-024 delta; `-UpgradeFromRc622` includes
022-024 for verified installed 001-021. The canonical current operator entry is
`docs/post-rc626-continuation-tr.md`. `scripts/powershell/Export-CompletionGuidance.ps1` exports that
entry, the single register, retained product evidence and supporting procedures
with their relative paths into a fresh operator directory. The top-level runbook
links to that tree; the same tree accompanies the DBA delta. Operator files are
hashed in release metadata. No corporate samples or server values are included.
`integrated-activation-tr.md` is historical rc6.26/023 guidance, no longer exported.
Matching InUseEvidence (including completion
mode) is built, scanned and hashed in `diagnostics/` by the same source commit.
Earlier 001-022/022-only descriptions below document old rc6.24 packaging, not
instructions to replay installed migrations. The tool does not deploy or enable
corporate effects. Prepare one successor only after applicable gates.

`New-PairedTestRelease.ps1 -ReleaseName <reviewed-next-name> -ExpectedSource <full reviewed HEAD>` requires a clean
committed feature branch (pre-existing `.vscode/` is excluded), publishes matching
API/UI/Worker on the combined delivery branch, reuses the payload scanners/validators,
exports the current Turkish entry and packages the exact reviewed SQL inventory
with per-file sizes/hashes and source metadata. Hangfire.SqlServer 1.8.6's original
schema-9 installation script is included separately for reviewed DBA provisioning;
runtime DDL stays disabled. New source supports native Windows Service and console
hosting. The exported worker-service-operations-tr.md procedure and service/runtime
dependency validation accompany the candidate; packaging never installs a service.
SCM/logoff/recovery acceptance remains required. The external In Use contract gate
does not indefinitely block a scoped Worker/fixes release, but unsupported In Use
completion stays disabled and UI/process/MIME release gates are not waived.
It refuses an existing release directory. Determine the next name from actual
release metadata first. It does not deploy, activate writes or certify TEST acceptance.
Payload success is not release approval: `readyForInstallation` stays false;
the release owner records required gates in `evidence/validation.json`. DoD still
requires repository-wide format success; historical scoped passes are not a waiver.
The top-level runbook binds the actual release name/build SHA. DBA 001-024
inclusion is a reference artifact, never an instruction to replay unchanged SQL.

For a verified rc6.22 with 001-021/Hangfire 9, `-UpgradeFromRc622`
exports the same current entry and emits only the additive 022-024 DBA delta.
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
ZIP/hash/configuration exclusion logic for the console/service-capable Worker. It checks
the Worker identity/runtime manifest, Hangfire/SQL dependencies and every declared
runtime/native/resource asset before packaging. The default remains Ui. Shared
scanning additionally rejects private key/certificate/database and fixture files.
It also requires the matching PowerShell SDK Management/Utility dependencies and
Windows module manifests; engine-only presence is insufficient for SCCM hosting.
The packaging script never installs a service or changes target configuration.
