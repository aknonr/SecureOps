# .NET 10 Backend Follow-ups Review - 2026-10-05

## 1. Verdict

The authorized revocation, pattern modernization and test-analyzer tasks pass local Windows
gates on SDK 10.0.401. This review records remaining work for owner approval. The review
commit changes documentation only and grants no merge, deployment or provider activation approval.

Review source: `bd92ce8bebc92995c172207afc3a8a8f0042fb73`, based on
`origin/claude/dotnet10-ui-mudblazor9` at `258d05f481c0bad6841359d8ac95245a4a021631`.
The static survey covered backend declarations and common synchronous/authentication hazards
in Domain, Infrastructure, API and Worker. Detailed inspection focused on sessions, access
administration, In Use services and job dispatchers. This is bounded source review; provider,
SQL failure injection and deployment topology remain unverified.

## 2. Evidence

### Missing Guarantees

**F1 - P1: Session termination and audit are separate persistence operations.**
`src/SecureOps.Infrastructure/Sessions/ApplicationSessionService.cs:215` ends a session before
the audit call at line 216; logout does the same at lines 136 and 137. The SQL update at
`src/SecureOps.Infrastructure/Sessions/SqlApplicationSessionRepository.cs:69` supplies no
transaction. `ApplicationSessionService.cs:333` writes through a separate audit abstraction.
An audit failure can therefore leave an ended session without its required audit event, while
the caller receives an unavailable result. This contradicts the same-transaction state/audit
rule in `docs/agent-guides/020-backend-dotnet.md`. Existing start-audit compensation tests
do not establish atomic terminal-state audit. Both paths predate these follow-ups.
Approval needed: a repository operation that commits the exact termination and append-only
audit together, with deterministic failure tests and separately authorized isolated SQL evidence.

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
Approval question: should missing store entries force explicit sign-in, or should correlation
be durable? Any implementation must update ADR-0014 and test old-cookie replay after loss.

**F3 - P2: Terminal repository results are ignored during concurrent termination.**
`SqlApplicationSessionRepository.cs:70` returns whether a row was actually ended.
`ApplicationSessionService.cs:215`, line 136 and line 305 ignore that boolean and build
results/audit from the previously read session. Concurrent logout/revoke/expiry can report or
audit a reason different from the reason that won in persistence, or duplicate a success event.
Approval needed: define the losing operation's response and reread/compare semantics before
changing the service contract. Concurrent terminal-state tests are missing; no SQL race was run.

**F4 - P2: In-flight validation has an undefined revocation boundary.**
`ApplicationSessionService.cs:110` treats a failed touch as a normal throttled update and
returns Active at line 115. `SqlApplicationSessionRepository.cs:52` rejects touches to ended
sessions. A revoke between the initial read and touch can therefore be followed by an Active
result for the already-running request. Requests validating after persisted revocation do
reject, as the hosted regressions verify. Approval needed: decide whether already-running
operations may finish, or require an additional atomic validation/action boundary. A second
unlocked read alone would not establish that stronger guarantee.

### Excess Coupling And Duplication

**F5 - P3: In Use application behavior depends on concrete SQL implementation types.**
`src/SecureOps.Infrastructure/InUse/InUseService.cs:52`, line 240 and line 305, and
`src/SecureOps.Infrastructure/InUse/InUseService.Catalogue.cs:18` retain concrete provider gates.
They intentionally distinguish SQL-backed identity/catalogue behavior from local substitutes;
the pattern-only commit preserves this distinction. These are maintenance coupling, not
evidence that authorization belongs in the UI. Approval needed before replacing the gates
with an explicit interface/composition contract; deleting them would change behavior.

**F6 - P3: Dispatchers resolve the same queue dependency twice.**
`src/SecureOps.Infrastructure/Announcements/Mail/AnnouncementMailWorker.cs:22` checks the
queue client, then resolves and casts it again at line 28. The equivalent In Use pattern is at
`src/SecureOps.Infrastructure/InUse/Execution/InUseExecutionWorker.cs:47` and line 53.
A future small cleanup can capture one typed client while retaining the configuration fence.
This is redundancy, not a demonstrated send/retry defect. Durable intents, disabled retries
and Unknown outcomes must remain. No code was removed during this review.

### Local Verification

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

## 3. Blockers

- F1 remains a security review blocker pending its approved atomicity implementation. F2 is
  resolved with hosted loss/replay evidence; actual IIS/browser lifecycle acceptance is separate.
- F3 and F4 require concurrency semantics before code changes.
- The 2026-10-02 admin-adjustable bounded session-policy decision remains separate work.
  Current policy is read from `SessionSecurityOptions` and exposed by `AccessController.cs:74`;
  this task implements the requested revocation portion, not a policy mutation endpoint.
- SQL, process acceptance, browser and corporate-provider evidence is incomplete. The 104
  opt-in cases were skipped. No database, IIS, service, binding or live system was changed.
  Playwright was neither installed nor run.

## 4. Minimal Safe Next Step

Implement the now-approved atomic session/audit change for F1 locally. F2's approved fail-closed
policy is implemented. Define F3/F4 semantics before implementation.
Amend ADR-0014 in any behavior-changing commit. F5/F6 can wait for those decisions.

## 5. Risks

Revocation is observed on the browser's next API operation. The existing circuit navigation
triggers an HTTP request that rejects/deletes the UI cookie. Idle browsers do not receive a new
polling mechanism. Credential clearing and old-cookie rejection are locally tested; multi-node
hosting, actual browser navigation and Windows/IIS/provider lifecycle behavior remain unproven.
Passing these local gates does not establish corporate readiness or remove the remaining F1 gate.
