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

Populated with identity lookup contracts, application-session contracts, audit-store health contracts, Operational Record/Jira response contracts, safe API error codes, authorization policy constants, and strongly typed session/Data Protection configuration options. Continue to keep this project free of infrastructure and UI dependencies.
