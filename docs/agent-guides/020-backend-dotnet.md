# 020 — Backend (.NET)

Owner: Codex. Read the README of each project you touch; it records current contracts and evidence.

## Build settings (from `Directory.Build.props` / `global.json`)

`net8.0`, C# 12 (`LangVersion` pinned), SDK pinned in `global.json` with roll-forward disabled, nullable enabled, warnings as errors, analyzers and code style enforced in build, XML docs required outside tests. Package versions are central in `Directory.Packages.props`. Do not change the toolchain without an ADR.

## Conventions that are specific here

- **Routes and errors.** Controllers live under `api/v1/<resource>` and return RFC 7807 `ProblemDetails` with a stable `code`, `stage` and `retryable`; never stack traces.
- **Authorization.** `[Authorize(Policy = Policies.CanX)]` (or the module's own policy class, e.g. `ServiceAccountPolicies`); policies are capability-based. Never check role or group names inline.
- **Data access.** SQL Server through Dapper with parameters only; schema changes are new numbered scripts in `sql/schema/` with their dependencies stated (see `sql/README.md`). Scripts are never applied by application startup; corporate execution goes through the approved DBA process. There is no EF Core model.
- **Concurrency.** Mutable aggregates carry a version; writes send the expected version and a stale write is a 409 that changes nothing.
- **Idempotency.** External writes (Jira, mail) are preview-first and use durable idempotency keys; an unknown outcome is never retried automatically.
- **Audit.** State changes and privileged reads write an audit event in the same transaction as the change.
- **Async and logging.** Propagate `CancellationToken` through I/O; no `.Result`/`.Wait()`. Structured logging with message templates; never log secrets or full personal data.
- **Contracts.** API shape changes update `docs/contracts/` and the OpenAPI snapshot in the same change, additively where possible.
