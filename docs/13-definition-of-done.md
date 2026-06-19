# 13 — Definition of Done

A single, reusable checklist for "done" at three levels: a code change, a phase, and the MVP.

## Per Code Change

A code change (PR, commit set, or single edit) is "done" when **all** are true:

### Build and Test

- [ ] `dotnet build SecureOps.sln` returns 0 errors and 0 warnings.
- [ ] `dotnet test SecureOps.sln` passes.
- [ ] New behavior has new tests.
- [ ] Modified behavior has updated tests.
- [ ] No analyzer warnings introduced.
- [ ] `dotnet format --verify-no-changes` passes.

### Code Quality

- [ ] All async I/O methods take and propagate `CancellationToken`.
- [ ] No `async void` (except event handlers).
- [ ] No `.Result` or `.Wait()`.
- [ ] No swallowed exceptions (catch must log and decide).
- [ ] No hardcoded secrets, paths, or magic numbers without explanation.
- [ ] Public APIs have XML doc comments.
- [ ] Naming follows `.cursor/rules/020-backend-dotnet-rules.mdc`.

### Security

- [ ] No new external network destinations added without ADR.
- [ ] No new write operations on target servers (MVP).
- [ ] No new database UPDATE/DELETE on audit tables.
- [ ] No new role bypass paths in authorization.
- [ ] If sensitive data flows through, logging is destructured properly.

### Audit

- [ ] Any new state-changing operation writes an audit entry.
- [ ] Any new privileged read writes an audit entry.
- [ ] Audit `Action` codes added to the canonical list in `docs/08-audit-model.md`.

### Documentation

- [ ] Behavior changes are reflected in the relevant `docs/*.md`.
- [ ] Architecturally significant decisions are recorded in `docs/adr/`.
- [ ] If a `.cursor/rules/*.mdc` rule changes, this is intentional and reviewed.
- [ ] README updated if user-facing surface changed.

### Scope

- [ ] Change belongs to the current phase (see `docs/02-roadmap.md`).
- [ ] No unrelated changes mixed in.
- [ ] Diff is reviewable: prefer < 400 lines, hard cap 1000.

## Per Phase

A phase is "done" when **all** are true:

### Functional

- [ ] All deliverables in the phase plan (`plans/PHASE-X-*.md`) are complete.
- [ ] All exit criteria in `docs/02-roadmap.md` are met.
- [ ] All tasks in the backlog (where applicable) are checked off or explicitly deferred.

### Quality

- [ ] All tests pass.
- [ ] Coverage targets met (Domain 90%, Application 80%, Infrastructure 60%).
- [ ] Performance targets met for the phase.
- [ ] Security review completed where required (Bilgi Güv / Siber Güv sign-off for phases with new data flows).

### Documentation

- [ ] All relevant `docs/*.md` reflect the phase's reality.
- [ ] ADRs in `docs/adr/` cover any new architectural decisions.
- [ ] Operational runbooks in `docs/runbooks/` updated.
- [ ] Phase retrospective written in `docs/retros/PHASE-X-retro.md`.

### Stakeholders

- [ ] Pilot operators have been informed of changes.
- [ ] Management updated on phase outcome.
- [ ] Next phase backlog reviewed.

### Operational

- [ ] System is stable in the test (or pilot) environment.
- [ ] No critical bugs open at phase close.
- [ ] Known issues documented in `docs/known-issues.md`.

## Per MVP (End of Phase 2)

MVP is "done" when:

### Functional

- [ ] Webhook → diagnostic → audit flow works end to end.
- [ ] All 6 diagnostic modules functional on pilot servers.
- [ ] Blazor UI deployed and usable.
- [ ] Pilot operators using the system daily for at least 2 weeks.

### Quality

- [ ] 99%+ webhook delivery success in the pilot period.
- [ ] Diagnostic jobs complete in < 30s for standard alarms.
- [ ] Zero unplanned impact on pilot servers.
- [ ] All tests pass.

### Audit and Compliance

- [ ] Audit trail complete (every operation persisted).
- [ ] Append-only enforcement verified.
- [ ] Bilgi Güvenliği and Siber Güvenlik have signed off.
- [ ] Internal audit has reviewed the audit format.

### Stakeholders

- [ ] Pilot operators report the system is useful (structured survey).
- [ ] Management demo delivered.
- [ ] Phase 3+ backlog committed.

### Documentation

- [ ] All MVP-relevant `docs/*.md` complete and current.
- [ ] ADRs cover all major decisions.
- [ ] Runbooks for ops, deployment, and audit query exist.
- [ ] Turkish management summary updated.

## Special Considerations

### Phase 1A IdentityLookup

Phase 1A code can be considered implemented when backend endpoint behavior, audit fail-closed behavior, safe metadata endpoints, rate limiting, provider input guards, and tests are complete. It is not production-ready until a real AD smoke test is completed with an approved read-only test account and the security review signs off the app-pool/service identity permission boundary.

### When a Second Developer Joins

- Pull requests become mandatory (no direct main commits).
- Code review on every PR.
- Pair programming for security-sensitive code.
- Update this DoD with code review check.

### When AI Phase Starts (Phase 7)

- AI-specific DoD items in `docs/10-ai-rag-strategy.md`.
- Masking verification on every prompt path.
- AI audit complete on every interaction.
- Self-hosted-only network rules verified.

### When Phase 8 Starts (Remediation)

- Snapshot verification on every write.
- Approval chain verified.
- Catalog limited to ADR-approved actions.
- Emergency abort tested.

## When You Cannot Tick a Box

If an item cannot be ticked:

1. State explicitly which item.
2. State why.
3. Propose how it will be remediated.
4. Decide with the user whether to proceed or block.

Never silently skip a DoD item.
