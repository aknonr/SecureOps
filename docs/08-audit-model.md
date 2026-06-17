# 08 — Audit Model

The audit subsystem is the project's most important non-functional feature. This document is the canonical specification.

## Principles

1. **Append-only.** UPDATE and DELETE on audit tables are blocked at the SQL layer.
2. **Complete.** Every state-changing operation and every privileged read writes an audit entry.
3. **Structured.** Audit entries follow a JSON schema; arbitrary text in `DetailsJson` is parseable.
4. **Long-lived.** Minimum 36-month retention.
5. **Tamper-evident.** Optional hash chain in Phase 4+ (`PrevHash` column) for tamper detection.
6. **Queryable.** Indexed for fast lookup by alert, actor, action, and time.
7. **Process auditing, not personnel monitoring.** UI and report framing enforces this.

## What Gets Audited

### Always

| Event | Source | Action code |
|---|---|---|
| Alarm received from monitoring | API webhook | `AlertReceived` |
| Alarm normalized and persisted | API | `AlertPersisted` |
| Alarm assigned to operator | API or UI | `AlertAssigned` |
| Alarm acknowledged | UI | `AlertAcknowledged` |
| Alarm status changed | UI/API | `AlertStatusChanged` |
| Alarm resolved | UI | `AlertResolved` |
| Alarm suppressed | UI | `AlertSuppressed` |
| Diagnostic job enqueued | API | `DiagnosticEnqueued` |
| Diagnostic job started | Worker | `DiagnosticStarted` |
| Diagnostic job completed | Worker | `DiagnosticCompleted` |
| Diagnostic job failed | Worker | `DiagnosticFailed` |
| Manual diagnostic triggered | UI | `DiagnosticManuallyTriggered` |
| Alarm viewed | UI | `AlertViewed` |
| Diagnostic result viewed | UI | `DiagnosticViewed` |
| Notification dispatched | Worker | `NotificationSent` |
| Notification failed | Worker | `NotificationFailed` |
| Turuncuhat EVT context fetched | API / Worker | `TuruncuhatEvtFetched` |
| Turuncuhat EVT closed from SecureOps | API / Worker | `TuruncuhatEvtClosed` |
| Turuncuhat action marker updated | API / Worker | `TuruncuhatActionMarked` |
| Identity lookup requested | API | `IdentityLookupRequested` |
| Identity lookup succeeded | API | `IdentityLookupSucceeded` |
| Identity lookup not found | API | `IdentityLookupNotFound` |
| Identity lookup failed | API | `IdentityLookupFailed` |
| Audit query executed | UI/API | `AuditQueried` |
| Configuration changed (admin) | UI | `ConfigurationChanged` |
| RBAC mapping changed (admin) | UI | `RbacChanged` |
| User session opened | UI | `SessionStarted` |
| User session ended | UI | `SessionEnded` |
| Login failed (informational) | UI | `LoginFailed` |
| Authorization denied | API/UI | `AuthorizationDenied` |
| Phase 7 AI prompt sent | AI service | `AiPromptSent` (separate AiAuditLog) |
| Phase 8 remediation requested | UI | `RemediationRequested` |
| Phase 8 remediation approved | UI | `RemediationApproved` |
| Phase 8 remediation executed | Worker | `RemediationExecuted` |
| Phase 8 remediation aborted | UI/Worker | `RemediationAborted` |

### What is NOT Audited

- Routine read of the alert list (covered by session entries).
- Health check endpoints.
- UI navigation events that do not access specific records.

## Audit Entry Schema

```csharp
public sealed record AuditEvent
{
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
    public required string Actor { get; init; }          // 'CONTOSO\jane.doe', 'system:api', 'system:worker'
    public required string Action { get; init; }        // from the table above
    public Guid? AlertId { get; init; }
    public string? ServerName { get; init; }
    public string? CorrelationId { get; init; }
    public string? SourceIp { get; init; }
    public object? Details { get; init; }                // serialized to JSON in DetailsJson
}
```

Serialized to JSON for `DetailsJson` column (see `docs/04-domain-model.md` for table DDL).

## DetailsJson Conventions

Each action defines its own `Details` shape:

```json
// AlertReceived
{ "externalId": "SW-ALERT-12345", "turuncuhatEvtId": "EVT-54321", "alertType": "Disk", "severity": "High" }

// DiagnosticStarted
{ "module": "DiskDiagnostic", "targetServer": "APPSRV-12" }

// DiagnosticCompleted
{ "module": "DiskDiagnostic", "durationMs": 4521, "inferredSeverity": "High" }

// AuditQueried
{ "filters": {"timeRange": "last24h", "actionType": "DiagnosticCompleted"}, "resultCount": 42 }

// AlertViewed
{ "viewedSection": "DiagnosticDetail" }

// TuruncuhatEvtFetched
{ "turuncuhatEvtId": "EVT-54321", "integrationMode": "api-pull" }

// TuruncuhatEvtClosed
{ "turuncuhatEvtId": "EVT-54321", "status": "Çözüldü" }

// TuruncuhatActionMarked
{ "turuncuhatEvtId": "EVT-54321", "actionTaken": true }

// IdentityLookupRequested
{ "normalizedAccount": "pam12356", "purpose": "EVT-54321 incident response verification", "turuncuhatEvtId": "EVT-54321" }

// IdentityLookupSucceeded
{ "normalizedAccount": "pam12356", "matchedAccount": "pam12356", "source": "ActiveDirectory" }

// IdentityLookupNotFound
{ "normalizedAccount": "pam12356", "source": "ActiveDirectory" }

// IdentityLookupFailed
{ "normalizedAccount": "pam12356", "source": "ActiveDirectory", "errorCode": "ProviderUnavailable" }

// AuthorizationDenied
{ "endpoint": "/api/v1/audit", "policy": "CanViewAudit" }
```

Document each shape in `contracts/schemas/audit-event.schema.json`.

## Writing Audit

A single class handles all writes:

```csharp
public interface IAuditWriter
{
    Task WriteAsync(AuditEvent evt, CancellationToken ct);
}
```

Implemented with Dapper for low-allocation inserts. Bulk write supported for high-volume scenarios.

```csharp
public sealed class SqlAuditWriter : IAuditWriter
{
    private readonly IDbConnectionFactory _connections;

    public async Task WriteAsync(AuditEvent evt, CancellationToken ct)
    {
        const string sql = @"
            INSERT INTO audit.AuditLog (OccurredAt, Actor, Action, AlertId, ServerName, CorrelationId, DetailsJson, SourceIp)
            VALUES (@OccurredAt, @Actor, @Action, @AlertId, @ServerName, @CorrelationId, @DetailsJson, @SourceIp);";

        await using var conn = await _connections.OpenAsync(ct);
        await conn.ExecuteAsync(sql, new
        {
            evt.OccurredAt,
            evt.Actor,
            evt.Action,
            evt.AlertId,
            evt.ServerName,
            evt.CorrelationId,
            DetailsJson = evt.Details is null ? null : JsonSerializer.Serialize(evt.Details),
            evt.SourceIp
        });
    }
}
```

## Querying Audit

Audit queries are themselves audited (meta-audit). The query endpoint:

- Requires `CanViewAudit` policy (Auditor or Admin).
- Logs an `AuditQueried` entry with the filter and result count.
- Returns rows with optional limit (default 1000, max 10000).
- Supports CSV export (which itself is audited).

Query parameters:
- Time range
- Action type
- Actor
- Alert ID
- Server name
- Correlation ID

## Append-Only Enforcement

SQL trigger:

```sql
CREATE TRIGGER audit.tr_AuditLog_BlockUpdateDelete
ON audit.AuditLog
INSTEAD OF UPDATE, DELETE
AS
BEGIN
    THROW 51000, 'Audit log is append-only.', 1;
END;
```

Test verifying enforcement:

```csharp
[Fact]
public async Task AuditLog_RejectsUpdate()
{
    var entry = await InsertAuditEntryAsync();
    var ex = await Assert.ThrowsAsync<SqlException>(
        async () => await UpdateAuditEntryAsync(entry.Id));
    ex.Message.Should().Contain("append-only");
}
```

## Retention

| Retention class | Duration | Mechanism |
|---|---|---|
| Operational audit | 36 months minimum | Partition by month + archive job |
| AI audit (Phase 7+) | 36 months minimum | Same partitioning |
| Remediation audit (Phase 8+) | 60 months | Same partitioning |

After the retention window, partitions are archived (not deleted) to cold storage. Cold storage location and access controls are defined per the organization's archive policy.

## Tamper Detection (Phase 4 Optional)

Optional hash chain:

```sql
ALTER TABLE audit.AuditLog ADD
    EntryHash       NVARCHAR(64) NULL,
    PrevEntryHash   NVARCHAR(64) NULL;
```

A nightly job computes hashes for the day's entries and stores them. Periodic verification recomputes and compares; mismatch raises an alert (which itself is audited).

If implemented, this is documented in an ADR with key management approach.

## Audit Is Not Surveillance — Reinforcement

Repeat from `docs/05-security-model.md`:

The audit subsystem is designed and labeled as **operational response verification**, **SLA evidence**, and **incident review** — not personnel performance monitoring.

### UI Constraints

- Default filters: time, action type, alert, server. Not operator.
- Operator filter available only with `CanViewAudit` policy.
- No leaderboards.
- No "fastest responder" widgets.
- No per-operator comparison reports in the default UI.

### Reporting Constraints

- Aggregate reports default to team level.
- Per-operator breakdown exists for compliance use, marked with watermarking ("INTERNAL AUDIT — NOT FOR DISTRIBUTION").
- HR briefed proactively that this is process auditing.

### Behavior on Misuse Attempt

If an Admin user attempts to extract a per-operator report, the system:
1. Allows the export (it is a legitimate compliance feature).
2. Writes an `AuditQueried` entry with `purpose` field required.
3. Watermarks the export with the requestor's identity.

## Common Audit Queries

For operations:

```sql
-- All actions on a specific alert
SELECT * FROM audit.AuditLog WHERE AlertId = @id ORDER BY OccurredAt;

-- All alarms on a specific server in last 30 days
SELECT * FROM audit.AuditLog
WHERE ServerName = @server AND OccurredAt > DATEADD(day, -30, SYSDATETIMEOFFSET())
ORDER BY OccurredAt DESC;

-- All audit queries (meta-audit) in last 7 days
SELECT * FROM audit.AuditLog
WHERE Action = 'AuditQueried' AND OccurredAt > DATEADD(day, -7, SYSDATETIMEOFFSET());
```

## Reference

- `docs/04-domain-model.md` — table DDL
- `docs/05-security-model.md` — authorization for audit access
- `contracts/schemas/audit-event.schema.json` — JSON schema
