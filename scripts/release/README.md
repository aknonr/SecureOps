# API Release Packaging

`New-InUseEvidencePackage.ps1 -OutputDirectory <new-absolute-directory>` builds
only the standalone diagnostic on the development machine from committed HEAD.
It uses the shared payload/secret scanner, publishes framework-dependent win-x64,
smoke-checks the no-network usage path, creates per-file size/SHA256 and runtime/
source metadata, and verifies every ZIP entry. It refuses existing destinations;
it does not replace rc6.14, publish API/UI, deploy, or collect corporate evidence.
The target needs both .NET 8 shared runtimes, not an SDK or repository. Operator
instructions and the `{}` dictionary are in `scripts/diagnostics/InUseEvidence`.

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
