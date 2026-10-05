# .NET 10 Backend Follow-ups Review - 2026-10-05

## 1. Verdict

The authorized revocation, pattern modernization and test-analyzer tasks pass local Windows
gates on SDK 10.0.401. Approved follow-up commits resolve F2 and F1 with hosted loss/replay
and actual isolated SQL transaction evidence. F3-F6 remain open. The original review commit
was documentation-only; this updated report grants no merge, deployment or provider activation.

Review source: `bd92ce8bebc92995c172207afc3a8a8f0042fb73`, based on
`origin/claude/dotnet10-ui-mudblazor9` at `258d05f481c0bad6841359d8ac95245a4a021631`.
The static survey covered backend declarations and common synchronous/authentication hazards
in Domain, Infrastructure, API and Worker. Detailed inspection focused on sessions, access
administration, In Use services and job dispatchers. Original source references below are
historical unless marked current. Corporate providers and deployment topology remain unverified;
isolated SQL failure injection is now verified by the approved F1 follow-up.

## 2. Evidence

### Missing Guarantees

**F1 - P1: Resolved; termination and append-only audit commit atomically.**
Evidence: `ApplicationSessionServiceTests.Termination_*` covers audit failure, update failure
and success for logout, revoke, idle/absolute expiry, expiry sweep and access-disable batches.
Hosted `TerminalAuditFailure_ReturnsRetryableUnavailableAndKeepsPersistedSessionActive` proves
retryable 503 responses for logout/revoke/expiry. The SQL repository uses one transaction for
updates and direct audit INSERT; InMemory stages state and requires atomic audit publication.
Six actual `ResourceSqlTests.Sessions_*` tests cover the same six-path matrix, second-audit
insert rollback of the first insert and both updates, cancellation, and true/false transition
results. `artifacts/test-results/f1-sql-final/` and `artifacts/session-localdb-final.log` retain
evidence. The complete guarded harness passed 56 SQL tests, zero skips, migrations 001-027.
ADR-0014 Amendment 3 records atomicity, supported compositions and F3/F4 exclusions.

Original finding (historical source references):
`src/SecureOps.Infrastructure/Sessions/ApplicationSessionService.cs:215` ends a session before
the audit call at line 216; logout does the same at lines 136 and 137. The SQL update at
`src/SecureOps.Infrastructure/Sessions/SqlApplicationSessionRepository.cs:69` supplies no
transaction. `ApplicationSessionService.cs:333` writes through a separate audit abstraction.
An audit failure can therefore leave an ended session without its required audit event, while
the caller receives an unavailable result. This contradicts the same-transaction state/audit
rule in `docs/agent-guides/020-backend-dotnet.md`. Existing start-audit compensation tests
do not establish atomic terminal-state audit. Both paths predate these follow-ups.
The owner approved the atomic repository operation and isolated SQL evidence; implemented above.

**F2 - P1: Resolved; missing UI process state now requires explicit sign-in.**
Evidence: `UiSignOutTests.MissingSessionEntry_OldCookieIsDeletedAndExplicitSignInGetsNewCorrelation`
replays the old UI cookie after entry removal and receives a login challenge plus cookie deletion.
`ApplicationSessionHostedTests.UiStoreLoss_ExistingCorrelationCannotStartAnotherApiSession`
simulates entry removal and fresh-store/cache loss against the hosted API; its session-start
audit count remains one. Transport and OIDC regressions pass (32 unit + 32 hosted integration,
0 skips; `artifacts/test-results/f2-final/`). Only explicit successful sign-in initializes an
active correlation. ADR-0014 Amendment 2 records the owner decision; no SQL correlation was added.

Original finding (historical source references):
`src/SecureOps.Ui/Hosting/UiOidcAuthentication.cs:79` calls `GetOrCreate` while validating an
existing authentication cookie. `src/SecureOps.Ui/Services/ApiSessionStore.cs:355` creates an
empty session when the cache entry is missing. With interim authentication, a still-valid UI
cookie can then cause a cookie-less API request; `ApplicationSessionService.cs:75` starts a
session when the presented handle is absent. After process restart or cache loss, a previously
revoked browser can therefore lose its terminal marker and create a fresh API session.
OIDC token loss follows its own rejection path; it is not evidence for interim-cookie safety.
The process-local transport and this missing-entry behavior predate the changes. Session v2
clears credentials and retains a terminal marker within the current process, but does not
introduce durable browser correlation. The loss/restart scenario was originally inspected, not run.
The owner chose explicit sign-in on missing state, not durable SQL correlation; implemented above.

**F3 - P2: Open; terminal repository results are ignored during concurrent termination.**
Current `SqlApplicationSessionRepository.cs:87` returns whether an audited operation actually ended a row.
`ApplicationSessionService.cs:213`, line 136 and line 313 ignore that boolean and build
results/audit from the previously read session. Concurrent logout/revoke/expiry can report or
audit a reason different from the reason that won in persistence, or duplicate a success event.
Approval needed: define the losing operation's response and reread/compare semantics before
changing the service contract. Sequential true/false repository tests now pass for both backends;
they do not define losing-operation policy or prove concurrent terminal-state behavior. No SQL race was run.

**F4 - P2: Open; in-flight validation has an undefined revocation boundary.**
`ApplicationSessionService.cs:110` treats a failed touch as a normal throttled update and
returns Active at line 115. `SqlApplicationSessionRepository.cs:52` rejects touches to ended
sessions. A revoke between the initial read and touch can therefore be followed by an Active
result for the already-running request. Requests validating after persisted revocation do
reject, as the hosted regressions verify. Approval needed: decide whether already-running
operations may finish, or require an additional atomic validation/action boundary. A second
unlocked read alone would not establish that stronger guarantee.

### Excess Coupling And Duplication

**F5 - P3: Open; In Use application behavior depends on concrete SQL implementation types.**
`src/SecureOps.Infrastructure/InUse/InUseService.cs:52`, line 240 and line 305, and
`src/SecureOps.Infrastructure/InUse/InUseService.Catalogue.cs:18` retain concrete provider gates.
They intentionally distinguish SQL-backed identity/catalogue behavior from local substitutes;
the pattern-only commit preserves this distinction. These are maintenance coupling, not
evidence that authorization belongs in the UI. Approval needed before replacing the gates
with an explicit interface/composition contract; deleting them would change behavior.

**F6 - P3: Open; dispatchers resolve the same queue dependency twice.**
`src/SecureOps.Infrastructure/Announcements/Mail/AnnouncementMailWorker.cs:22` checks the
queue client, then resolves and casts it again at line 28. The equivalent In Use pattern is at
`src/SecureOps.Infrastructure/InUse/Execution/InUseExecutionWorker.cs:47` and line 53.
A future small cleanup can capture one typed client while retaining the configuration fence.
This is redundancy, not a demonstrated send/retry defect. Durable intents, disabled retries
and Unknown outcomes must remain. No code was removed during this review.

### Original Local Verification

All commands ran from `C:\SecureOpsBuild\secure-ops-dotnet10-backend-followups`:

| Gate | Result |
|---|---|
| `dotnet build SecureOps.sln -c Release` | Passed, 0 warnings, 0 errors, SDK 10.0.401 |
| `dotnet test SecureOps.sln -c Release --no-build` | 1,725 unit and 317 integration passed; 104 opt-in integration cases skipped |
| `dotnet format SecureOps.sln --verify-no-changes` | Passed repository-wide |
| Session/OIDC/sign-out focused run | 41 unit and 28 hosted integration passed; 0 skips, overlapping the full-suite totals |
| `git diff --check` | Passed |
| Fetch and `git merge-tree --write-tree --messages HEAD origin/claude/dotnet10-ui-mudblazor9` | Passed without conflicts against base 258d05f; no branch merge performed |

Local ignored TRX evidence: `artifacts/test-results/session-v2/`,
`artifacts/test-results/backend-patterns/`, `artifacts/test-results/final/`.
The xUnit1051 suppression was removed from both projects; omitted optional tokens in flagged
calls now use `TestContext.Current.CancellationToken`. Dedicated cancellation inputs remain.
The build catches the diagnostic without a replacement suppression.

### Approved F2/F1 Follow-up Verification

All commands ran from the same isolated worktree with SDK 10.0.401:

| Gate | Result |
|---|---|
| `dotnet build SecureOps.sln -c Release` | Passed, 0 warnings, 0 errors |
| `dotnet test SecureOps.sln -c Release --no-build` | 1,752 unit + 324 integration passed; 110 opt-in cases skipped in this run |
| `dotnet format SecureOps.sln --verify-no-changes` | Passed repository-wide after correcting one import-order diagnostic |
| `git diff --check` | Passed |
| `Test-ResourceCatalogueSql.ps1 -DatabaseSuffix SessionFinal_20261005_f821770ce3d5 -RunTests` | Upgrade 001-027 and all 56 Resource SQL tests passed, 0 skips |

The 56 SQL cases were skipped in the ordinary suite and then actually run separately; these
totals do not overlap. The remaining 54 opt-in cases were not run (other SQL/process/acceptance
facilities were not enabled). Focused F2 totals overlap the full suite. Ignored evidence:
`artifacts/test-results/f2-red/`, `f2-final/`, `f1-red/`, `f1-sql-red/`, `f1-batch-red/`,
`f1-focused-final/`, `f1-sql-final/`, `followups-final/` beneath `artifacts/test-results/`.

Red-before-fix evidence reproduced four F2 failures, all six unit termination/audit failures,
actual SQL logout rollback failure, and partial batch append with the original audit writer.
The final diff was reviewed for awaits, cancellation, rollback scope, atomic publication,
credential/log leakage and backend parity. No test hook or failure trigger was added to runtime.
SQL fixtures and test-created triggers stay within the guarded facility; test triggers are
dropped in `finally`. Both isolated databases are retained for inspection:

- `SecureOps_ResourcesV1_SessionAtomic_20261005_983d69252cb8` (red/green focused evidence).
- `SecureOps_ResourcesV1_SessionFinal_20261005_f821770ce3d5` (fresh full SQL harness).

## 3. Blockers

- F1/F2 are resolved within the approved scope; actual IIS/browser lifecycle acceptance is separate.
- F3 and F4 require concurrency semantics before code changes.
- The 2026-10-02 admin-adjustable bounded session-policy decision remains separate work.
  Current policy is read from `SessionSecurityOptions` and exposed by `AccessController.cs:74`;
  this task implements the requested revocation portion, not a policy mutation endpoint.
- Other SQL/process opt-ins, browser and corporate-provider evidence remain incomplete. Only
  the authorized LocalDB test facility was written; no corporate database, IIS, service, binding
  or live system was changed. Playwright was neither installed nor run.

## 4. Minimal Safe Next Step

Review the two separate local F2/F1 commits and the prepared draft PR text. The current task
explicitly forbids push/PR creation. Define F3/F4 semantics before implementation; F5/F6 wait.

## 5. Risks

Revocation is observed on the browser's next API operation. The existing circuit navigation
triggers an HTTP request that rejects/deletes the UI cookie. Idle browsers do not receive a new
polling mechanism. Credential clearing and old-cookie rejection are locally tested; multi-node
hosting, actual browser navigation and Windows/IIS/provider lifecycle behavior remain unproven.
InMemory/File or queued audit cannot share atomic session persistence and now fails closed
for termination; shipped defaults were not changed. Initial-session insertion/failed-start
compensation was not redesigned. Passing local gates does not establish corporate readiness
or resolve F3/F4. No simultaneous-termination or already-running request policy is claimed.
