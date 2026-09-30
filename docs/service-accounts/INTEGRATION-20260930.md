# Pinned Windows Integration Evidence

This is supporting evidence, not another requirements register. Current status:
[integrated-test-activation.md](../integrated-test-activation.md).

## Source and Preservation

- New branch: `feature/service-accounts-pinned-integration-20260929`.
- New worktree: `C:\SecureOpsBuild\secure-ops-sa-pinned-integration-20260929`.
- Platform baseline: `e997c5b68cebcd23716860a9b06fdc25ebbb4493`.
- Integrated pin: `b4fdf8d439990032fb5f4c21486030bdb903a27c`.
- Module measured code: `b8aad7edd68e6e35438ba0b8516ea25dc4e2ad3b`.
- Recovery transport: `service-accounts-followup-20260930-7e227ed.bundle`.
  Measured SHA-256:
  `31B0E3899BA0BE77E9B3F2FF9D39534907CA7A767F6D27A57D19FE9CAC93A99B`.
  Git verified complete history. Its tip was retained only as a review ref;
  the requested b4fdf8d ancestor was selected, not any later follow-up code.
- Merge base is e997c5b; a fast-forward in the NEW branch was sufficient.
  No Jira UI patch was reapplied, no conflict resolution invented DTO fields.
- Original SDM, UI, Claude worktrees, recovered branches and sealed artifacts
  are untouched. No release package was generated or rehashed.

The build/tests below ran on pinned b4fdf8d product content. Later changes in
this worktree are retained preflight scripts and documentation only. OpenAPI
regeneration is semantically identical to the pin (no product change).
The new build's API entry DLL ProductVersion is
`0.1.0+b4fdf8d439990032fb5f4c21486030bdb903a27c`; it is not the sealed
`deda8486` delivery and was not packaged or installed.

## Shared and SQL Review

The pin adds only module wiring to API/Worker Program, UI client/navigation,
capability catalog, scoped error handling and module field labels. It preserves
the integrated Worker Windows Service lifetime, content-root handling,
diagnostics, process lock and single existing job server. Five existing Worker
composition tests passed in the full unit run: disabled registration/stale-job
no-op, enabled schedule and repeated module-owned schedule behavior. No Worker
process or service was started.

OpenAPI regenerated through the actual combined Program test host. Comparison
against e997c5b: 44 added paths, zero removed/changed existing paths or schemas.
Comparison against b4fdf8d: full semantic equality. Raw formatting/line endings
are not an additional contract change.

Unnumbered SQL remains outside release discovery. SA-001 starts its transaction
before schema creation and commits after the final protective trigger; SQLCMD
must use fail-fast `-b` so a failed connection closes without committing a
partial module. Successful fresh installation and replay refusal ran below.
The older failure-injection evidence remains historical; it was not rerun here.
The API role grants only the module DML verbs used by its repository, plus
security.Users SELECT and audit INSERT; only uncommitted ImportRows may be
deleted. Worker role: Accounts/WorkRequests SELECT and ReminderOutbox
SELECT/INSERT/UPDATE. No TeamMemberships grant, broad schema control, DDL,
corporate identity/member or migration number was added. Role-script installation
is NOT a Windows least-privilege runtime test (the local runner owns its database).

The module-specific connection pool and explicit READ COMMITTED reset avoid
leaking SERIALIZABLE into platform sessions. Import commit's exclusive gate and
ordinary account writers' shared gate retain the pinned deadlock fix; the
parallel first-run SQL tests passed without added retries/sleeps/weaker asserts.

## Executed Windows Evidence

Private evidence root: `C:\SecureOpsBuild\validation\sa-pinned-20260930`.
Sibling logs `sa-pinned-*-20260930.log` retain command output.

| Check | New result | Retained evidence |
|---|---|---|
| Release no-incremental solution build | 0 warnings, 0 errors | `sa-pinned-build-20260930.log` |
| All unit tests | 1568 passed, 0 failed/skipped | `unit.trx` |
| Fresh module harness | 001-024 + SA candidate + unassigned API/Worker roles; replay refused | `sa-pinned-db-20260930.log`; DB `SecureOps_SaPinned0930A` |
| Normal integration, first module SQL run | 316 passed, 61 opt-in skipped, 0 failed | `integration-first-run.trx` |
| Module subset within that run | 33 passed (30 SQL, 3 HTTP composition); NOT added to 316 | Same TRX |
| Safe SQL failure diagnostics | Only intentional Number=51091, State=1, Class=16 audit-rollback entry; no private parameters/connection string | `sa-diagnostics.log` |
| Platform ResourceSql on separate fresh 001-024 DB | 49 passed, 0 failed/skipped | `platform-resource-sql.trx`; DB `SecureOps_ResourcesV1_SaPinnedSdm0930A` |
| OpenAPI generation/compatibility | 1 passed; semantic checks above | `openapi-regeneration.trx` |
| Permission-query syntax/result shape | Read-only local execution succeeded; 13 permission rows | `local-permission-query.txt`; local DB owner session only, NOT target API evidence |

Module tests include persisted access-service/role-bundle composition without
access wrappers, participant/cross-team denial, scoped evidence and reporting,
import replay/concurrency/audit rollback and scenario 4's eight open plans plus
two review requests with no performed action/verified closure. HTTP composition
tests cover anonymous/capability denial and endpoint policy metadata, not the
allowed SQL-backed HTTP/browser pilot matrix. Opt-in skipped tests were not run
and historical counts are not added to this evidence.

## Unresolved and Unexecuted

- The pin's two older isolated first-run failures remain unexplained. This new
  first run passes; that does not identify their lost exception/cause.
- The pin permits a Review action to satisfy IsVerifiedClosure when tagged
  Closure with generic verification facts (ServiceAccountRules.cs); scenario
  4's existing test checks untouched open requests, not that transition. The
  corresponding follow-up is intentionally outside this integration; Claude
  must hand it over separately before claiming that stronger business guard.
- No restricted Windows API/Worker-principal runtime test, allowed SQL-backed
  normal-auth HTTP/UI matrix, actual browser journey, IIS or desktop Excel run.
  The older capability-injecting browser harness is limited historical evidence.
- Corporate bundles, scope grants, pilot identities/retention and change approval
  remain owner inputs; no module activation or corporate data was used.
- The SDM target API SQL rights gate is separate, as are In Use IU-05 and Falcon.
  No target SQL, flags, source/Jira mutation, mail, push or master merge occurred.
