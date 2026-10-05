# ADR-0028: Approved switch of service account components to a gMSA (PROPOSED)

**Status:** Proposed — draft for the approval decision. Not accepted, not implemented, nothing deployed. It asks for an
**exception to the Phase 8 pre-conditions** for one narrow catalog; without that written exception it stays a design.
**Date:** 2026-10-06
**Decision makers (required):** project owner and IT management (Phase 8 pre-condition exception, in writing);
Bilgi Güvenliği and Siber Güvenlik (write endpoint, identities); change advisory board (OCO process for each run);
server owners (pilot waves); AD team (gMSA objects, outside this system).
**Related:** ADR-0002, ADR-0006 (this is a catalog addition under it), ADR-0024 revision 2 (read side, prerequisite),
ADR-0027, `docs/09-snapshot-change-safety.md`, `docs/service-accounts/ops-research/`.

## Context

The team's own PowerShell tool finds where an account runs and then re-stamps a **new password** into every Windows
service, IIS application pool, IIS "connect as" setting, scheduled task and COM+ application, through unconstrained remote
sessions, with no preview, approval, audit, verification or rollback, printing the password in places that logs keep
(analysis: `ops-research/01-script-farki.md`, risks R1–R8). The project owner wants the platform to do this work, and better.

Owner statement 2026-10-06: **password changes are the PAM team's obligation**; the platform does not change passwords.
What remains is the strategic fix the module already tracks: converting accounts to **gMSAs**, whose password is managed
by AD and retrieved by the host — no person, no tool and no log ever holds it. Today that switch is done by hand, server by
server, which is slow and error-prone. This ADR proposes that the platform performs it, under ADR-0006's guarantees.

## Decision (proposed)

### 1. Catalog — three actions, nothing else

| Action | Changes | Restart | Rollback without a password |
|---|---|---|---|
| `ServiceLogonToGmsa` | `Win32_Service` StartName → `DOMAIN\gmsa$`, empty password | stop / start, bounded wait for Running | **No** — the former password is in PAM; revert is manual (§6) |
| `AppPoolIdentityToGmsa` | `processModel` → `SpecificUser`, `DOMAIN\gmsa$`, no password attribute | recycle | **Yes** — the element is restored from a backup kept on that server (§6) |
| `ScheduledTaskPrincipalToGmsa` | principal → `DOMAIN\gmsa$`, logon type Password (gMSA, no password supplied) | none (next run) | **No** — manual, as for services |

Out of the catalog, shown as "manual" in every preview: IIS "connect as" (needs a password; gMSA is not usable there —
the application must move to pass-through authentication), COM+ (gMSA support unverified), any password change, granting
user rights, creating gMSAs or changing `PrincipalsAllowedToRetrieveManagedPassword` (AD team), disabling or deleting the
former account (a later, separate module action with its own OR, unchanged).

### 2. The write endpoint cannot distribute passwords — by construction

- Separate session configuration `SecureOpsServiceAccountChange`, separate role capability, `RestrictedRemoteServer`,
  `RunAsVirtualAccount`, transcripts on. The diagnostic and read endpoints stay read-only.
- Connects as its own gMSA (placeholder `CONTOSO\gmsa-so-sa-change$`) in its own group; the read identity of ADR-0024 cannot
  use it.
- Three visible functions, one per action. **No function has a password, credential or script parameter.** The new
  identity must match `^[A-Za-z0-9][A-Za-z0-9_.-]{0,14}\\[A-Za-z0-9_.-]{1,15}\$$` (a gMSA); built-in and local identities are
  refused. So even a stolen change identity cannot plant a password or set an ordinary user account.
- **Compare-and-swap.** Every call carries `-ExpectedCurrent` (the identity the approved preview saw). The function re-reads
  the component first; if it is not exactly that identity, it changes nothing and answers `PreconditionChanged`. This
  removes the tool's worst risk (re-stamping a component that has since moved to another account).
- Every call carries `-ChangeId` (the platform's execution step id), written with the before/after identity to the
  server's Application event log under a fixed source, so the server side can be correlated with the platform audit.
- Answers are typed: `Changed`, `AlreadyTarget` (idempotent re-run), `PreconditionChanged`, `NotFound`, `RestartFailed`,
  `Failed` (exception type only, never its message), each with before/after identity and state.

### 3. Flow in the module — plan → preview → approval → run → verify → (rollback)

1. **Plan** — `ServiceAccounts.ChangePlan` (new) plus the responsible basis on every account; 1–20 accounts. Items come only
   from a JEA scan (ADR-0024 R2) not older than 24 hours; a person cannot type a component. The requested gMSA name
   (migration 031) is the target.
2. **Preview** — per item: server, type, name, current → target identity, restart effect, and blocking pre-checks:
   AD allows the server to retrieve the gMSA (ADR-0024 R2-3); the gMSA holds "log on as a service / batch job" on that
   server (read only — the platform never grants it); the server answered the latest scan; the type is in the catalog.
   A blocked item cannot be approved; the preview says how to clear it and who does (AD team, GPO owner).
   The preview has a version; any change invalidates approvals (same pattern as the import commit).
3. **Approval** — `ServiceAccounts.ChangeApprove` (new); **approver ≠ planner** (enforced in the service and by a SQL
   check); OCO number required; maintenance window (start/end); justification. An approval expires at window end.
4. **Run** — started inside the window by the planner or approver; a Hangfire job dispatched by approval id:
   canary (1 server) → waves of at most 5 servers → the rest, at most 16 servers in parallel; on one server, items run one by
   one in a fixed order (tasks, app pools, services). It **stops** on any canary failure, on 3 failures or > 10 % failed
   items, and waits for the approver to resume or end. Administrators can abort at any time (in-flight calls finish, no new
   call starts). Each step is idempotent (`AlreadyTarget`), so a resumed run never applies a step twice.
5. **Verify** — right after the run, an automatic JEA scan with `expectedAccount` (ADR-0027 §6 outcomes) plus the state of
   each changed service and pool. The module records the gMSA conversion action as **Performed** with the run as evidence;
   **Verified** stays a person with `ServiceAccounts.Verify` who is not the planner (SPEC rules 3–4 unchanged).
6. **Rollback** — app pools: `Restore-…` from the on-server backup (§6), same approval, same flow. Services and tasks: the
   module lists exactly what to set back; a person reverts with the former password checked out from PAM. The former
   account is not changed, disabled or deleted by this ADR, so rollback stays possible during the observation period.

### 4. Audit and records

Append-only tables (migration number assigned at implementation): `ChangePlans`, `ChangePlanItems` (preview snapshot:
before/after, pre-check results), `ChangeApprovals`, `ChangeRuns`, `ChangeStepResults` (server, component, before, after,
outcome type, duration), `ChangeAborts`. Every transition writes module history and `audit.AuditLog` in the same
transaction (`ServiceAccount.ChangePlanned/Approved/RunStarted/StepCompleted/RunPaused/RunAborted/RunCompleted`). No secret,
no exception message, no free-text from servers. Operational evidence only — no per-person views (rule 5).

### 5. Speed

A step is seconds plus the restart; 200 components on 50 servers are expected in well under an hour inside a window
(estimate, to be measured in the pilot). The limit is safety (waves, stop rules), not throughput.

### 6. Snapshot policy (deviation from ADR-0006 item 3, needs explicit approval)

ADR-0006 requires a verified VM snapshot before any write. For this catalog the proposal is a **configuration-level
backup instead**: the app-pool function copies the pool's `processModel` element (with IIS's own encrypted values, never
decrypted, never returned) to a folder readable only by Administrators/SYSTEM on that server, kept 30 days; services and
tasks rely on the unchanged former account plus PAM. A VM snapshot per server for an identity switch is heavier than the
change itself; if Bilgi Güvenliği prefers it, `ISnapshotInspector` (Phase 5) becomes a pre-check instead.

## Alternatives considered

- **Re-stamp passwords like the team tool:** rejected — passwords would travel to every server and through the product; the
  owner placed password changes with PAM.
- **Default-endpoint remoting with the operator's credentials:** rejected (rule 3; the tool's main risk).
- **Platform reads the former password from PAM for automatic rollback:** possible later, needs a BeyondTrust integration
  ADR (rule 7); not part of this proposal.
- **Generic "set identity" function:** rejected — the narrow gMSA pattern is what makes the endpoint harmless.

## Consequences

- Positive: conversions become fast, uniform, previewed, approved, audited and verified; no password exists anywhere in
  the flow; components that moved meanwhile are never overwritten.
- Negative: a write endpoint on managed servers (rule 1 exception, Phase 8 pull-in); new capabilities and roles; a
  maintenance window and an approver per run; services/tasks have no automatic rollback.

## Required before implementation

1. Written Phase 8 pre-condition exception for this catalog (project owner + IT management), recorded here.
2. ADR-0024 revision 2 approved and piloted (the read side and pre-checks are prerequisites).
3. Bilgi Güvenliği and Siber Güvenlik approval of §2 and §6, recorded here.
4. Worker access path decided (`docs/15-system-landscape.md`); the diagnostic allow-list finding closed.
5. Pilot plan with server owners: one non-production server, then canary waves; CAB process agreed for OCO numbers.
6. Estimate: 25–35 developer days after approval (module ≈ 20, endpoint and packaging ≈ 5, pilot support), split in
   `docs/service-accounts/ops-research/05-yol-haritasi.md` (aşama C).
