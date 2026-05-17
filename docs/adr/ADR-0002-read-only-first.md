# ADR-0002 — Read-Only First

**Status:** Accepted
**Date:** 2026-05
**Decision makers:** Project owner, with anticipated security team validation

## Context

The system has technical capability to execute PowerShell on target Windows servers. PowerShell can do anything: restart services, recycle app pools, delete files, modify configurations, reboot.

A natural early design might include "auto-remediation" — when a disk-full alarm fires, automatically delete temporary files; when a service fails, automatically restart it.

But:

- A single developer cannot validate every remediation path safely.
- Stakeholder trust must be earned incrementally.
- Production servers serve real workloads; an incorrect automatic action could cause an outage.
- Security teams (Bilgi Güvenliği + Siber Güvenlik) have not yet approved any write operations.
- The audit story is cleaner if writes don't exist yet.
- Demonstrating value through investigation alone is sufficient for MVP.

## Decision

**MVP (Phases 1–6) performs zero write operations on target servers.**

The system:
- Receives alarms.
- Runs **read-only** PowerShell cmdlets through a JEA constrained endpoint.
- Persists results and audit data.
- Surfaces findings to humans.
- Generates ticket text, shift reports, and notifications.

The system does NOT:
- Stop, start, or restart services.
- Recycle, stop, or start app pools.
- Delete, modify, or create files.
- Reboot, shut down, or suspend servers.
- Modify groups, permissions, or registry.
- Push configurations or patches.

This is enforced at multiple layers:

1. **JEA endpoint** — the service account literally cannot run write cmdlets.
2. **Code layer** — application code does not construct write commands.
3. **Documentation** — `.cursor/rules/050-security-audit-rules.mdc` makes the rule explicit for agents.
4. **Tests** — security tests verify forbidden cmdlets are blocked.

Write operations are introduced **only in Phase 8** through an approval-based workflow (see ADR-0006).

## Alternatives Considered

### Auto-Remediation in MVP

Rejected. Reasons:
- Cannot validate every path under single-developer constraint.
- No stakeholder approval.
- Higher impact if wrong.
- Demos and pilot value do not require it.

### Manual Remediation Button in UI in MVP

Rejected. Reasons:
- Buttons that trigger writes are writes. Same risk.
- A "small" UI button can become a "small" outage.
- Approval workflow is a Phase 8 design problem.

### Limited Write (e.g., service restart only) in MVP

Rejected. Reasons:
- One exception breaks the rule.
- "Just service restart" still has cascading impact.
- The discipline of "no writes" is easier to defend than "no writes except X".

### "We'll think about it later"

Rejected. Reasons:
- A decision left implicit is a decision left dangerous.
- Phase 8 with explicit ADR is the right venue.

## Consequences

### Positive

- Drastically reduced operational risk during MVP.
- Easier security review (clear story: nothing changes).
- Cleaner audit narrative for stakeholders.
- Builds trust incrementally.
- Aligns with the "kişi takibi değil" framing — system observes, doesn't command.

### Negative

- Some "obvious" automation (clean tempfile when disk full) is deferred.
- Operators continue manual action; only the diagnosis is automated.
- ROI is purely from investigation time savings, not from action automation.

### Neutral

- Phase 8 remains a clear future deliverable when conditions are right.
- The architecture supports adding write operations later without restructuring.

## Implementation Notes

- JEA whitelist in `docs/05-security-model.md` enumerates allowed cmdlets.
- Forbidden cmdlets are documented and tested.
- Worker code does not import write-capable modules.
- UI does not surface any "remediate" actions in MVP.

## References

- `docs/05-security-model.md` (JEA section)
- `docs/09-snapshot-change-safety.md` (Phase 8 design)
- `.cursor/rules/050-security-audit-rules.mdc`
- `ADR-0006-approval-based-remediation.md`
