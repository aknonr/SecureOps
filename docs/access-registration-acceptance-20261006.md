# G-34: local acceptance, 2026-10-06

Branch: `fix/access-user-registration-deadlock-20261006`.
Base: master commit `8032c80cd85f82c200a68dc92684e9308a09e687` (refreshed origin/master).
Worktree: `C:\SecureOpsBuild\secure-ops-access-deadlock-20261006`.
Windows, user-local SDK **9.0.317**, net8.0; no SDK/language override.

## Verdict and cause

The defect was reproduced before the production fix. Red test commit `4acbb10`
with 001-031 ran ten rounds of eight concurrent first registrations on one fresh
database: **73 successes, seven SQL 1205 failures, seven deadlock graphs**.
Graphs identify the exact latest-request read with RangeS-U owners/waiters on
`security.AccessRequests.IX_AccessRequests_StatusPage`; they prove cycles between
scans, not the originally suspected insert-conversion sequence.
The test-only 50 ms Users-insert delay ensures overlap and is removed in `finally`.
An earlier unmodified-timing run also captured 26 graphs; passing untimed replays
confirmed the issue is timing-sensitive.

Migration 032 alone, before the retry change, passed **80/80, zero deadlock graphs**.
The final cached plan uses **Index Seek on IX_AccessRequests_UserRequested**.
This proves the local cause and remedy, not every possible corporate SQL deadlock.

The binary retries only the rolled-back registration transaction on 1205, once.
Post-commit reads and all other operations are outside the retry. The second 1205,
non-deadlock SQL errors and cancellation are not concealed. No isolation, validation,
authorization, pending-user or unique-request safeguard was relaxed.

## Verified commands and results

Set `DOTNET_ROOT` to `$env:LOCALAPPDATA\Microsoft\dotnet` and prepend that directory
to PATH for all commands below.

| Check | Command / configuration | Result |
|---|---|---|
| Release build | `dotnet build SecureOps.sln -c Release` (final repeat with `--no-restore`) | 0 warnings, 0 errors |
| Full format gate | `dotnet format SecureOps.sln --verify-no-changes` | Exit 0; repository-wide, no include restriction |
| Unit suite | `dotnet test tests/SecureOps.Tests.Unit/SecureOps.Tests.Unit.csproj -c Release --no-build --no-restore` | 1918 passed, 0 failed, 1 skipped |
| Integration, opt-ins absent | `dotnet test tests/SecureOps.Tests.Integration/SecureOps.Tests.Integration.csproj -c Release --no-build --no-restore` | Earlier four-regression-test revision: 303 passed, 0 failed, 121 skipped |
| Final integration with Service Accounts SQL enabled | Same integration command, `SECUREOPS_SA_SQL_TEST_CONNECTION` targeting fresh synthetic `SecureOps_SaAccessG34_20261006` through 032 | 358 passed, 0 failed, 68 skipped (426 total) |
| Final G-34 SQL harness | `powershell -NoProfile -File tests/sql/access-registration/Test-AccessRegistrationSql.ps1 -DatabaseSuffix Final20261006b` | 6 passed, 0 failed/skipped; 80/80 concurrent registrations, zero workload deadlocks |

The unit skip deliberately avoids reading this Windows machine's real services/tasks.
The 68 integration skips include the six G-34 cases executed separately above,
other Resource/Announcement/Access SQL or process/OIDC opt-ins, and the separate
Service Accounts pre-031 upgrade fixture. No skipped case is counted as passed.
55 existing Service Accounts SQL cases ran in the final integration suite; its code
and serial collections were not edited. The existing harness created their fresh
database through 031 with role scripts, then only 032 was applied to that new database.

The six G-34 cases cover concurrent first registration/idempotent replay, one real
engine victim recovering on attempt two, two victims propagating 1205 after exactly
two attempts with no partial user/request/history, non-1205 failure without retry,
durable rejection/disabled access, and cancelled registration without persistence.
Deliberately forced deadlocks in the negative probes are expected; the normal
concurrency workload and the full integration run showed no 1205.

032 preserves synthetic Pending/Approved/Rejected access rows and all database
permissions by SHA-256; replay refuses with 51381. Metadata confirms the enabled,
nonunique, unfiltered `(UserId ASC, RequestedAt DESC) INCLUDE (Status)` index.
Temporary G-34 triggers and the capture session were absent after the final harness.

Evidence under `artifacts/access-registration/` (ignored, local):
`Red20261006f/{registration.trx,deadlocks.xml}`, index-only `Green20261006a/`,
`Final20261006b/{registration.trx,deadlocks.xml,latest-request.sqlplan,032-replay.log}`,
and `validation/{build-verified.log,format-verified.log,unit-final.trx,integration-sql-final.trx}`.
An initial unit migration-count mismatch and later test line-ending diagnostics were
corrected; the final build/unit/format results above supersede those failures.

## Boundaries and next step

No installed TEST upgrade, restored TEST backup, corporate provider/send, IIS/service
configuration, deployment, merge, Linux or browser/UI journey was performed.
The original checkout and its untracked files were preserved; changes are in the
separate worktree. Service Accounts/UI and both serial collection safeguards are unchanged.

Review the branch before any merge. Target DBA approval, installed-schema inventory,
recovery point and blocking/log/plan rehearsal remain outside this task.
See [DBA note](access-registration-dba-032.md), [G-34](26-ui-backend-contract-gaps.md#g-34--concurrent-first-registrations-can-deadlock-in-the-access-store)
and [SQL inventory](../sql/README.md). No further implementation decision is open;
retain the Service Accounts serialization measure until its separate owner decision.
