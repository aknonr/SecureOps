# Phase 8 — Approval-Based Remediation

**Duration:** 6–8 weeks
**Goal:** Enable approved, audited write operations on target servers — only with two-person approval and snapshot verification.

## Hard Pre-Conditions

**Phase 8 does not start until all of these are true:**

1. [ ] Phase 1–6 stable in production (at least 6 months).
2. [ ] Stakeholder approval (Bilgi Güv, Siber Güv, IT management) for write operations.
3. [ ] Approval workflow design reviewed and approved.
4. [ ] Snapshot/rollback capability verified for pilot servers.
5. [ ] ADR-0006 updated with concrete initial catalog and rollout plan.

## Hard Constraints (Reaffirm from ADR-0006)

- **Bounded catalog.** Only ADR-approved actions are available. Initial catalog: `ServiceStart`, `AppPoolStart`, `LogFileTruncate` (very small allow-list).
- **Two-person rule.** Operator requests; TeamLead approves. Same person cannot do both.
- **Snapshot verification.** Per `docs/09-snapshot-change-safety.md` policy.
- **Justification required.** Free-text on every request and approval.
- **Separate JEA endpoint.** `SecureOps.RemediationEndpoint` distinct from diagnostic endpoint.
- **Full audit chain.** Request → Approval → Execution → Outcome.
- **Emergency abort.** Admin-only halt for in-flight remediation.

## Deliverables

1. Remediation domain model: Request, Approval, Execution.
2. `ISnapshotInspector` adapter (read-only).
3. Separate JEA endpoint: `SecureOpsRemediationRole.psrc`.
4. UI workflow: request form, approval queue, execution view.
5. Worker job: `ExecuteApprovedRemediationJob`.
6. Audit chain: `RemediationRequested`, `RemediationApproved`, `RemediationExecuted`, `RemediationAborted`.
7. Emergency abort UI and worker mechanism.
8. Initial catalog: `ServiceStart`, `AppPoolStart`, `LogFileTruncate`.

## Task Breakdown

### Sprint 1 — Domain and Storage (~1 week)

| # | Task | Estimate |
|---|---|---|
| P8-T01 | `Remediations` table + EF entity | 4h |
| P8-T02 | `RemediationActionCatalog` table + seed | 3h |
| P8-T03 | `Approvals` audit entries | 2h |
| P8-T04 | Domain validation: cannot self-approve | 3h |

### Sprint 2 — Snapshot Adapter (~1 week)

| # | Task | Estimate |
|---|---|---|
| P8-T05 | `ISnapshotInspector` + `MockSnapshotInspector` | 3h |
| P8-T06 | Real adapter (vSphere or Hyper-V) | 8h |
| P8-T07 | Snapshot age policy enforcement | 4h |
| P8-T08 | Tests | 4h |

### Sprint 3 — JEA Remediation Endpoint (~1 week)

| # | Task | Estimate |
|---|---|---|
| P8-T09 | `SecureOpsRemediationRole.psrc` with `Start-Service`, `Start-WebAppPool` | 6h |
| P8-T10 | `Install-SecureOpsRemediationEndpoint.ps1` | 4h |
| P8-T11 | Deploy to one pilot server | 4h |
| P8-T12 | Verify diagnostic endpoint still read-only | 2h |

### Sprint 4 — Workflow and UI (~2 weeks)

| # | Task | Estimate |
|---|---|---|
| P8-T13 | UI: Request form (operator) | 8h |
| P8-T14 | UI: Approval queue (lead) | 6h |
| P8-T15 | Notification: request → lead | 3h |
| P8-T16 | UI: Approval action with justification | 4h |
| P8-T17 | Worker: execution job | 8h |
| P8-T18 | UI: Execution status / outcome | 6h |
| P8-T19 | Emergency abort UI and mechanism | 6h |

### Sprint 5 — Tests and Rollout (~1 week)

| # | Task | Estimate |
|---|---|---|
| P8-T20 | Tests: self-approve rejected | 2h |
| P8-T21 | Tests: snapshot policy enforcement | 3h |
| P8-T22 | Tests: emergency abort | 3h |
| P8-T23 | Tests: full chain audit verified | 3h |
| P8-T24 | Documentation: operator guide | 4h |
| P8-T25 | Documentation: lead approval guide | 3h |
| P8-T26 | Pilot rollout (1 server, then expand) | 8h |
| P8-T27 | Stakeholder sign-off | 2h |

**Total Phase 8 estimate:** ~130 hours, ~7 weeks at 18h/week.

## Initial Catalog (At Phase 8 Start)

| Action | Allowed cmdlet | Snapshot required |
|---|---|---|
| ServiceStart | `Start-Service` (specific service name from allowed list) | Yes |
| AppPoolStart | `Start-WebAppPool` (specific name) | Yes |
| LogFileTruncate | Controlled stub clearing specific path | Yes |

**Not in initial catalog:**
- `Stop-*`, `Restart-*`
- `Remove-Item`
- `Restart-Computer`
- Registry writes
- Configuration changes

Adding to catalog requires its own ADR.

## Exit Criteria

- [ ] No remediation executes without approval.
- [ ] Self-approval blocked.
- [ ] Snapshot verification enforced.
- [ ] Every step audited with full chain.
- [ ] Emergency abort tested.
- [ ] Initial catalog limited to 3 actions.
- [ ] Pilot operator + lead engineer trained.
- [ ] DoD satisfied.

## Risks

| Risk | Mitigation |
|---|---|
| Approval workflow misuse | Catalog bounded; meta-audit; sign-off required |
| Snapshot policy fails (no snapshot when needed) | Block remediation; surface to operator |
| Emergency abort race conditions | Test thoroughly; transactional state |
| Stakeholder concerns about writes | Bounded catalog; pilot first; expand slowly |
| Operator over-uses Remediation feature | Metrics + lead review |

## After Phase 8

The platform now has:
- Read-only diagnostics (Phase 1)
- Web UI (Phase 2)
- Notifications + ticket draft (Phase 3)
- Audit + verification (Phase 4)
- Compliance reports (Phase 5)
- Rule-based analysis (Phase 6)
- Optional AI assistance (Phase 7)
- Approval-based remediation (Phase 8)

Future work: expand the remediation catalog one ADR at a time. Each new action requires:
- ADR justifying need.
- Bilgi Güv approval.
- JEA cmdlet addition with whitelist update.
- Test coverage.
- Catalog entry with snapshot policy.

The platform never auto-acts. Every write remains human-decided.
