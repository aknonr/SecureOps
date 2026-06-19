# SecureOps.Shared

DTOs, JSON contracts, authorization policy constants, utilities.

## What goes here

- **Contracts** (`Contracts/` namespace): Request/response DTOs as `record` types. These mirror the JSON schemas in `contracts/schemas/`.
- **Auth** (`Auth/` namespace): `Policies` class with authorization policy name constants. Roles map to AD groups via `dbo.RbacRoles`.
- **Configuration** (`Configuration/` namespace): Strongly-typed options classes for `IOptions<T>` binding.
- **Utilities**: cross-cutting helpers with no external dependencies.

## Dependencies

- `SecureOps.Domain` only.

## Current state

Populated during Phase 1A with identity lookup contracts, audit-store health contracts, API error envelopes, authorization policy constants, and strongly typed configuration options. Continue to keep this project free of infrastructure and UI dependencies.
