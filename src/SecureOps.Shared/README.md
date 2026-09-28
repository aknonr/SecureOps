# SecureOps.Shared

InUseExecutionIntent freezes verification mode with historical SourceReadback
default; new submissions select Manual. Execution exposes immutable SourceCode.
ManualVerification evidence carries trusted confirmer identity/label and event UTC
time; it is distinct from Closure/Verified and never changes the original initiator.

Archive catalogue contracts retain original metadata and label current lifecycle
and attachment state separately. Report PreparedByAccount is frozen for new
archives and remains absent for unknown history. Reporter suggestion decisions
carry source scope/relation/version, reviewed mapping revision/expiry, candidate
identity and the trusted accepting/rejecting/overriding actor. Neither contract
grants authority or represents remote closure.

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
## E-08 In Use contract clarification

`ReviewSourceVersion` separates answer freshness from the full execution source
version. Null on historical records fails conservatively to the full version.
`WasasActivity`/`CurrentStage` are optional authoritative observations; the current
corporate adapter does not supply them. `ActivityVerifiedAt` is a server-side SQL
event projection, never caller attestation. New intent `WasasActivityManual` and
activity-specific manual evidence do not reinterpret historical OR closure events.
`InUsePersonLabel` keeps stable application GUIDs/SIDs out of human-facing labels;
new draft/report snapshots retain name/account with stable technical identity.

`InUseQuery.Sort` accepts code/oldest/newest (default code). Date ordering is
retained for later verified mapping, not a delivery prerequisite. `ActivityStatus` is a
minimal list classification: Pending, Completed, OrClosed or VerificationPending.
`HasActiveExecution` is a repository read-side fence; `SourceObservationMissing`
marks a parent not observed in the latest refresh. Neither manual confirmation nor
missing rows proves completion. These fields do not authorize command execution.
