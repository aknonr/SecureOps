# Identity Infrastructure

Codex-owned backend identity lookup. `IIdentityLookupService` normalizes, audits, and coordinates `IPamAccountResolver` and `IIdentityDirectoryProvider`. `ActiveDirectoryIdentityDirectoryProvider` is the implemented read-only exact-account provider; `IActiveDirectoryLookupClient` isolates `PrincipalContext` transport so local tests use fakes only.

`IdentityLookup:Provider=Mock` is the default. `ActiveDirectory` requires `IdentityLookup:DomainName`; `Container` is optional. sAMAccountName is queried exactly first, including PAM-style identifiers such as `pam000001`; UPN fallback is optional. The mock indexes its deterministic seed identities by exact sAMAccountName and, when `EnableUpnLookup=true`, exact UPN. No shell, PowerShell, wildcard, owner heuristic, or vendor-specific PAM behavior is used.

`GET /api/v1/identity/lookup/capabilities` reports the active provider's effective UPN behavior. `supportsUpnLookup` is not configuration intent alone: it is true only when the selected provider can perform exact UPN lookup and that behavior is enabled.

Infrastructure DI activates the default mock provider through an explicit options-aware factory. Constructors that accept a supplied user set are test seams only; runtime DI must not infer mock seed data from `IEnumerable<DirectoryUserRecord>`. The Active Directory provider and transport each have one public constructor and are selected only by `IdentityLookup:Provider=ActiveDirectory`.

`IPamAccountResolver` remains a Mock-only extension boundary. A real PAM adapter is planned only after an approved contract. Local development has no corporate AD/PAM access and must never contact it. Controlled test-server validation must use the application runtime identity, approved domain/container, DC reachability, exact lookup, timeout, and authorization checks.

API release packaging must pass `scripts/release/Validate-ApiAdRuntimeDependencies.ps1`. It verifies the SHA256 manifest, unique ZIP paths, publish-to-ZIP hashes, net8.0 runtime manifests, and the complete `System.DirectoryServices.AccountManagement` dependency runtime closure without contacting a domain controller.

The TEST runtime keeps exact Active Directory lookup settings external to the package. `docs/24-api-test-deployment-readiness.md` is the authoritative environment-variable manifest; no domain or container value is embedded in release files.
