# ADR-0006 — Approval-Based Remediation

**Status:** Accepted (conditional; Phase 8 only)
**Date:** 2026-05
**Decision makers:** Project owner; final write-operation rollout subject to security team and management approval

## Context

After Phase 6, with rule-based analysis surfacing patterns and recurring problems, the natural next step is to act on those findings — restart a stopped service, recycle a stuck app pool, clear a known-safe log file.

But automated remediation has a long history of unintended consequences. A "small fix" applied widely can cause an outage. A misclassified alarm can trigger the wrong action.

ADR-0002 established read-only MVP. This ADR establishes how writes are introduced when the time comes.

## Decision

**Phase 8 introduces write operations only through an approval-based workflow with these properties:**

1. **Bounded catalog.** Only ADR-approved actions are available. Initial catalog is small (e.g., `ServiceStart`, `AppPoolStart`).
2. **Operator request, lead approval.** Two-person rule: operator requests, TeamLead approves.
3. **Snapshot verification.** Before any write, the system verifies a recent snapshot exists (per the configured policy in `docs/09-snapshot-change-safety.md`).
4. **Justification required.** Free-text justification on every request and every approval.
5. **Separate JEA endpoint.** A separate `SecureOps.RemediationEndpoint` with its own role capability file. The diagnostic endpoint remains read-only.
6. **Full audit chain.** Audit entries for request, approval, execution, outcome. Approval ID tied to execution.
7. **Emergency abort.** Admin can halt in-flight remediation.

The system **never** performs writes automatically based on rules. Every write is human-decided.

## Alternatives Considered

### Automatic remediation when rule conditions match

Rejected:
- No way to validate every path safely.
- One misclassification = outage.
- Stakeholder trust insufficient.
- Contradicts the "kişi takibi değil" framing — also reinforces operator agency.

### One-step "Remediate" button (no approval)

Rejected:
- Removes the second-person check.
- A click is an action; mistakes happen.
- Approval workflow is the human-in-the-loop pattern that works.

### Allow remediation for any cmdlet the operator could run

Rejected:
- Loses the bounded-catalog benefit.
- JEA whitelist exists for a reason; expansion is gated.
- Approval workflow only meaningful if scope is bounded.

### Different approval workflows per action type

Considered, deferred:
- Adds complexity.
- Initial catalog is small enough for one workflow.
- Can be revisited when catalog grows.

## Consequences

### Positive

- Writes are deliberate, audited, and reversible.
- Stakeholders can trust the system because automation is bounded.
- The pattern scales: new actions added via ADR.
- Operator agency preserved.

### Negative

- Adds workflow overhead per write operation.
- Requires lead engineer availability for approvals.
- More complex to implement than auto-remediation.

### Neutral

- The catalog grows over time, not all at once.

## Implementation Notes (For Phase 8)

- `Remediations` table tracking request → approval → execution.
- Notification to lead engineer at request time (Teams).
- UI for lead engineer to approve / reject / request more info.
- Worker job for execution, dispatched by approval ID.
- Audit entries: `RemediationRequested`, `RemediationApproved`, `RemediationExecuted`, `RemediationAborted`.

## Initial Catalog (At Phase 8 Start)

| Action | Notes |
|---|---|
| ServiceStart | Only for services in an allowed-list, e.g., common app services |
| AppPoolStart | IIS app pool that is stopped |
| LogFileTruncate | Specific paths only, allowlisted |

Each catalog entry has its own mini-ADR adding it to the canonical list.

### Initially NOT in Catalog

- `Stop-*` of any service.
- Any `Remove-*`.
- `Restart-Computer`.
- Registry writes.
- Configuration changes (IIS, services, scheduled tasks).

## References

- `docs/09-snapshot-change-safety.md`
- `docs/08-audit-model.md` (Phase 8 audit actions)
- `ADR-0002-read-only-first.md`
