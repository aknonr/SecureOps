# 09 — Snapshot and Change Safety

Pre-Phase 8 scaffolding for safe remediation. Most of this document is forward-looking — it describes what must be in place **before** the first write operation runs.

## When This Applies

- Not in Phase 1–6 (no writes at all).
- Foundational work in Phase 5 (snapshot adapter, read-only).
- Operational in Phase 8 (approval-based remediation).

## Core Principle

**No write operation runs without verified rollback capability.**

This means before any approved remediation executes:

1. The target VM has a recent snapshot OR the operation is in an explicit "safe without snapshot" list.
2. The snapshot is within the configured age window.
3. The operator and approver acknowledge the snapshot state.
4. The remediation is recorded with the snapshot reference.

## Snapshot Adapter (Phase 5+)

Read-only adapter to the virtualization platform:

```csharp
public interface ISnapshotInspector
{
    Task<SnapshotInfo?> GetLatestSnapshotAsync(string serverName, CancellationToken ct);
    Task<IReadOnlyList<SnapshotInfo>> ListSnapshotsAsync(string serverName, CancellationToken ct);
}

public sealed record SnapshotInfo(
    string Id,
    string Name,
    DateTimeOffset CreatedAt,
    long SizeBytes,
    string? Description);
```

Supported platforms (to be confirmed per environment):
- VMware vSphere
- Hyper-V

The adapter never creates, deletes, or reverts snapshots. Those operations remain in the hands of the virtualization team.

## Snapshot Policy Configuration

```json
{
  "ChangeSafety": {
    "MaxSnapshotAgeHours": 24,
    "RequireSnapshotFor": [
      "ServiceRestart",
      "AppPoolRecycle",
      "RegistryWrite"
    ],
    "SnapshotOptionalFor": [
      "DiskFreeSpaceCleanupReadOnlyQuery"
    ]
  }
}
```

Operations not on either list default to **require snapshot**.

## Approval-Based Remediation Flow (Phase 8)

```
1. Operator sees a finding.
2. Operator clicks "Request Remediation".
3. UI shows the remediation form:
   - Action selected from approved catalog
   - Target server confirmed
   - Snapshot state shown (auto-fetched)
   - Justification text required
4. Operator submits → audit entry RemediationRequested.
5. Notification to TeamLead (Teams).
6. TeamLead reviews:
   - Re-fetches snapshot state at approval time
   - Confirms or rejects
7. Approval → audit entry RemediationApproved.
8. Worker executes via JEA (extended whitelist for this action).
9. Result captured → audit entry RemediationExecuted with outcome.
10. UI shows result; another optional verification step may follow.
```

Every step is audited. No step is skipped.

## Catalog of Approved Remediation Actions (Phase 8 Initial)

A **bounded** set, intentionally small:

| Action | Description | Snapshot required | JEA cmdlet |
|---|---|---|---|
| ServiceStart | Start a stopped Windows service | Yes | `Start-Service` |
| AppPoolStart | Start an IIS app pool | Yes | `Start-WebAppPool` |
| LogFileTruncation | Clear contents of a specific log file (path allowlisted) | Yes | controlled stub |

NOT in the initial catalog:
- `Stop-*` of any service (risk of cascading impact)
- `Remove-Item` of anything
- `Restart-Computer`
- Configuration changes (registry, IIS config, etc.)

Each addition to the catalog requires an ADR.

## JEA Whitelist Extension for Phase 8

When Phase 8 begins, the JEA endpoint is extended with **a separate role capability file**: `SecureOpsRemediationRole.psrc`. The diagnostic endpoint remains unchanged.

Two separate endpoints:
- `SecureOps.DiagnosticEndpoint` — read-only, available now.
- `SecureOps.RemediationEndpoint` — write-restricted, available in Phase 8 after approval.

The Worker explicitly chooses the endpoint based on the operation. Diagnostic jobs use the diagnostic endpoint. Remediation jobs use the remediation endpoint and require an approval ID at job enqueue time.

## Emergency Abort

The system supports an immediate halt of in-flight remediation:

- UI button visible to Admin only.
- Sets a flag in SQL; the Worker checks before each step.
- Audit entry `RemediationAborted` with abort reason.

## Reference

- `docs/05-security-model.md` — JEA approach
- `docs/08-audit-model.md` — audit entry types for remediation
- `docs/adr/ADR-0006-approval-based-remediation.md` — decision record
