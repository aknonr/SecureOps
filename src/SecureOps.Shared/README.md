# SecureOps.Shared

In Use report contract now separates corporate Sheets from internal EvidenceSheets
and freezes SourceCode for readable UTC download names. Historical reports recover
the code only from their own Provenance sheet. Draft lifecycle adds Discarded,
InvalidatedReviewsThrough and trusted last-action metadata; none denotes an external
undo or source closure. InUseServerReview.Invalidated is a current projection over
immutable history, not a rewrite of the saved answer. See ADR-0020/OpenAPI.

DTOs, JSON contracts, authorization policy constants, utilities.

`Contracts/Resources/` defines bounded catalogue and personal-set requests and
response envelopes. Resources.View and Resources.Manage are distinct capabilities.

## What goes here

- **Contracts** (`Contracts/` namespace): Request/response DTOs as `record` types. These mirror the JSON schemas in `contracts/schemas/`.
- **Auth** (`Auth/` namespace): `Policies` class with authorization policy name constants. Roles map to AD groups via `dbo.RbacRoles`.
- **Configuration** (`Configuration/` namespace): Strongly-typed options classes for `IOptions<T>` binding.
- **Utilities**: cross-cutting helpers with no external dependencies.

## Dependencies

- `SecureOps.Domain` only.

## Current state

Populated with identity lookup contracts, application-session contracts, audit-store health contracts, Operational Record/Jira response contracts, safe API error codes, authorization policy constants, and strongly typed session/Data Protection configuration options. Continue to keep this project free of infrastructure and UI dependencies.
