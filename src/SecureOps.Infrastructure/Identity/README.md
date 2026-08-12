# Identity Infrastructure

Codex-owned backend identity lookup. `IIdentityLookupService` normalizes, audits, and coordinates `IPamAccountResolver` and `IIdentityDirectoryProvider`. `ActiveDirectoryIdentityDirectoryProvider` is the implemented read-only exact-account provider; `IActiveDirectoryLookupClient` isolates `PrincipalContext` transport so local tests use fakes only.

`IdentityLookup:Provider=Mock` is the default. `ActiveDirectory` requires `IdentityLookup:DomainName`; `Container` is optional. sAMAccountName is queried exactly first, including PAM-style identifiers such as `pam000001`; UPN fallback is optional. No shell, PowerShell, wildcard, owner heuristic, or vendor-specific PAM behavior is used.

`IPamAccountResolver` remains a Mock-only extension boundary. A real PAM adapter is planned only after an approved contract. Local development has no corporate AD/PAM access and must never contact it. Controlled test-server validation must use the application runtime identity, approved domain/container, DC reachability, exact lookup, timeout, and authorization checks.

API release packaging must pass `scripts/release/Validate-ApiAdRuntimeDependencies.ps1`. It verifies the SHA256 manifest, unique ZIP paths, publish-to-ZIP hashes, net8.0 runtime manifests, and the complete `System.DirectoryServices.AccountManagement` dependency runtime closure without contacting a domain controller.

The TEST runtime keeps exact Active Directory lookup settings external to the package. `docs/24-api-test-deployment-readiness.md` is the authoritative environment-variable manifest; no domain or container value is embedded in release files.
