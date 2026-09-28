# OR-to-SDM Jira-only ServerRequest UI — Cloud Continuation Handoff

Date: 2026-09-28. Branch: `feature/ui-or-sdm-jira-only-confirmation-20260928`, based on
`5c986a9` (`feature/combined-test-delivery-20260915` HEAD at the time). Scope is UI only.

## Ownership (read first)

- **Codex owns the backend and its integration.** Codex is actively working, uncommitted, in the
  backend delivery worktree on `feature/combined-test-delivery-20260915`. This branch contains
  none of that work and must not absorb it. Do not edit API, domain, infrastructure, SQL,
  contracts, or OpenAPI from this branch; report contract needs instead (`CLAUDE.md`).
- Integration order is Codex's call: backend commits first, then these UI commits are
  cherry-picked or rebased onto it. A local dry run applied the backend snapshot over these
  changes with no conflict; the only shared file is `src/SecureOps.Ui/README.md` (disjoint hunks).
- No merge, deployment, flag activation, corporate Jira/source call, or release package was made.

## Instructions and plan that govern this UI

- `AGENTS.md` (root; there is no nested AGENTS.md), `CLAUDE.md`
- `docs/agent-guides/README.md`, `060-ui.md`, `050-security-audit.md`, `090-testing-quality.md`
- `src/SecureOps.Ui/README.md` → "Operational Record → Jira" and "Jira-only submission states"
- `docs/22-operational-record-jira-workflow.md`, `docs/adr/ADR-0009-operational-record-jira-workflow.md`
- `docs/contracts/secureops-api-v1-ui-integration.md` (Operational Record section) and
  `docs/contracts/secureops-api-v1.openapi.json`; there is no separate OR-to-SDM contract file.
- Canonical register `docs/integrated-test-activation.md` (SDM-01 rows) and the operator
  procedure `docs/post-rc626-continuation-tr.md` → "SDM-01". The 2026-09-28 SDM-01 revisions of
  both, and of docs/22 and ADR-0009, exist only uncommitted in Codex's worktree and are **not** on
  this branch. Read them from Codex's branch once committed.
- `090-testing-quality.md` caps a diff at 1,000 lines. The user granted a task-specific exception
  for this audit scope and this handoff (about +1.9k/−0.2k, over half of it tests).

## Current UI behavior

`OperationalRecordDetail.razor` → `JiraSubmissionPanel` (phase from `JiraSubmissionView.PhaseOf`):

| Phase | Shown when |
|---|---|
| Ready | server preview held, workflow permits create, operator has create capability |
| Submitting | create/retry in flight |
| Created | the **re-read** record has `jiraIssueKey`/`jiraExists`; never from a command response alone |
| Acknowledged | command answered but the re-read record has no persisted key |
| Rejected | server refusal, no key, outcome not unknown |
| Uncertain | `reconciliationRequired`, key-less `CreatingJira`, `stage=jira-reconciliation`, or `WorkflowAlreadyInProgress` |
| ReviewOnly / Unavailable | review-only draft / anything else, with the server-derived reason |

- Ready shows source OR and source id, request type and basis, Jira project/issue type/id, all
  preview fields, the transfer key as duplicate protection, and a prominent source-open statement
  from `preview.sourceCloseRequested`. Transfer-and-close is never offered; unsupported types show
  server blocker guidance only.
- Create and retry both require ticking a statement that names the OR and the source outcome
  (`JiraCreateDialog`, `JiraRetryDialog`, `SoConfirmStatement`). Retry is gated because for a safe
  `JiraCreateFailed` the server reports `retryEligible=true` and `POST /retry` calls Jira create.
- Under reconciliation neither create nor retry is offered, even if a payload contradicts the
  contract with `retryEligible=true`. Uncertainty from an unacknowledged command persists until
  an explicit **Yenile**. A lost response that the re-read resolves to a saved key is Created with
  a resolution note, without a stale "may or may not exist" notice.
- No Jira link is rendered; the DTOs carry no URL.

## Backend dependencies — proposals, not implemented DTO fields

None of these exist in the current DTOs or in Codex's uncommitted changes; the UI does not read them.

1. `jiraIssueUrl` (nullable, server-derived https) on `OperationalRecordResponse` and `JiraTransferResponse`.
2. Transfer key and UTC key-persisted time on `OperationalRecordResponse`.
3. A replay indicator on `JiraTransferResponse` (e.g. `replayed`).
4. **Retry preview**: a read-only view of the persisted reviewed draft for `JiraCreateFailed`, so the
   retry confirmation can show what Jira will receive. Today it cannot re-show fields.
5. Keep the deterministic actor/command/target fallback command key for create/retry (see below).
6. The SDM-01 procedure asks the operator to preserve the command identity; no DTO exposes it.

## HTTP replay experiment (network boundary: UI host → API host)

Tested boundary: the server-side hop from the Blazor Server UI host's `HttpClient`
(`SocketsHttpHandler`) to the API, via a test-only loopback proxy (`tests/browser/sdm-fault-proxy.cjs`,
`announcement-hosts.ps1 -UiApiProxyPort`). The browser ↔ UI SignalR hop, IIS, and the corporate
HTTPS load balancer were **not** tested.

- When the connection closes before any response byte, .NET resent the same `POST .../jira`
  automatically. A standalone probe reproduced it on fresh and reused connections and with
  `PooledConnectionLifetime = Zero` and `Connection: close`. A custom connection stream suppressed
  it only on a first-use plain-HTTP connection, not on reuse, and would not see HTTP bytes under
  TLS, so nothing was shipped.
- Observed result: two POSTs reached the API, one `JiraCreated` and one transfer persisted. Duplicate
  protection therefore depends on the API's **durable** SQL command idempotency and transfer
  uniqueness (dependency 5). This conflicts with the SDM-01 procedure wording "send exactly one
  create"; Codex should reconcile the procedure or the transport.

## Known UI limitation: uncertainty after F5

Preview and last-command outcome live in the Blazor circuit only. A full page reload (F5) after an
unacknowledged command loses the local Uncertain state; the phase then comes from persisted state
alone. If the request never started server-side, the page returns to "take a preview", and a
resubmission relies on dependency 5. Durable server state (`CreateRequested`/`CreatingJira`/
`reconciliationRequired`) is unaffected.

## Test evidence (Windows runner, 2026-09-28)

| Check | Result |
|---|---|
| `dotnet build SecureOps.sln -c Release` (this branch) | 0 warnings, 0 errors |
| Unit tests (this branch) | 1429/1429 |
| Combined dry-run tree (this branch + Codex snapshot): build / unit / integration | 0/0; 1511/1511; 281 passed, 61 SQL opt-ins skipped, 0 failed |
| SDM `ResourceSqlTests` (5 cases) on fresh test-owned LocalDB | 5/5 |
| `tests/browser/sdm-jira-only.cjs` (LocalDB, Simulation) | 6/6; `--verify-presentation` after real host restart passed |
| `tests/browser/sdm-jira-negative.cjs` via fault proxy | 5/5 |
| `tests/browser/sdm-jira-submission.cjs` | 6/6 |

Earlier failing runs are not counted; they led to the retry gate, the `WorkflowAlreadyInProgress`
fix and the stale-notice fix. Desktop 1440×900 and 390×844 screenshots showed no overflow.

**Not executed:** corporate SDM-01 acceptance (selected OR, destination, actor required); screen
reader; dark theme; the Submitting phase in a real browser (Simulation is too fast; unit-tested);
F5 during an in-flight command; any cloud/Linux run of these suites.

## Checks that must stay with the Windows runner

Unless equivalent execution is actually available, do not report these as run from cloud:
LocalDB (`(localdb)\SecureOpsResourcesV1`, `scripts/powershell/Test-ResourceCatalogueSql.ps1`,
`SECUREOPS_SQL_TEST_CONNECTION` SQL opt-ins), `sqlcmd`, `tests/browser/announcement-hosts.ps1`
(PowerShell, `FileSystemDpapi` data protection), the three browser journeys (installed Chrome at the
Windows path plus a local `playwright-core`), IIS/package verification, and any corporate TEST step.
Cloud can edit UI code, run `dotnet build`, and run unit tests if the SDK is present; report exactly
what ran.

Replay commands are in `src/SecureOps.Ui/README.md` → "Jira-only submission states". Evidence
(screenshots, TRX, logs) stays in the Windows runner's local validation root and is not committed.
