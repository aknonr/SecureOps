# API Release Packaging

`New-ApiDeploymentPackage.ps1` creates a path-preserving API ZIP and SHA256 payload manifest from a completed publish directory. It excludes controlled deployment configuration (`web.config` and `appsettings*.json`) and refuses to overwrite existing artifacts.

Before packaging, `Test-ApiReleasePayload.ps1` rejects PDB, source, project, test, log, `bin`, and `obj` payloads. It scans text files for credential-like assignments and can scan every publish file for caller-supplied ASCII and UTF-16 personal-path markers through `-ForbiddenText`.

`Validate-ApiAdRuntimeDependencies.ps1` then verifies that the manifest exactly represents the publish tree, ZIP paths and hashes match the manifest, required API/runtime files are present, API and Infrastructure assemblies share the expected dependency graph, and the complete `System.DirectoryServices.AccountManagement` runtime dependency closure is represented by the `.deps.json` and package assets.

These scripts do not publish, deploy, or modify server configuration. A package is not release-ready unless validation succeeds.
