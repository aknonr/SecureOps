# Identity Infrastructure

Codex-owned backend identity lookup. `IIdentityLookupService` normalizes, audits, and coordinates `IPamAccountResolver` and `IIdentityDirectoryProvider`. `ActiveDirectoryIdentityDirectoryProvider` is the implemented read-only exact-account provider; `IActiveDirectoryLookupClient` isolates `PrincipalContext` transport so local tests use fakes only.

`IdentityLookup:Provider=Mock` is the default. `ActiveDirectory` requires `IdentityLookup:DomainName`; `Container` is optional. sAMAccountName is queried exactly first, including PAM-style identifiers such as `pam000001`; UPN fallback is optional. No shell, PowerShell, wildcard, owner heuristic, or vendor-specific PAM behavior is used.

`IPamAccountResolver` remains a Mock-only extension boundary. A real PAM adapter is planned only after an approved contract. Local development has no corporate AD/PAM access and must never contact it. Controlled test-server validation must use the application runtime identity, approved domain/container, DC reachability, exact lookup, timeout, and authorization checks.

API release packaging must pass `scripts/release/Validate-ApiAdRuntimeDependencies.ps1`. It verifies `System.DirectoryServices.AccountManagement.dll` is present in both publish output and ZIP with assembly version `8.0.0.1` or later.
