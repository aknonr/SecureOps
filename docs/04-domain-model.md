# 04 — Domain Model

Domain model and database schema. The model is intentionally compact for MVP; later phases extend it.

## Bounded Contexts

| Context | Owns | Tables |
|---|---|---|
| Alerting | Alarms received and their lifecycle | `Alerts`, `AlertEvents` |
| Diagnostic | Job orchestration and results | `DiagnosticJobs`, `DiagnosticResults` |
| Inventory | Pilot server registry | `Servers` |
| Application access | Authentication-independent users, approval requests, roles, capabilities | `security.Users`, `security.AccessRequests`, `security.Roles`, `security.RoleAssignments` |
| Audit | Append-only operational audit | `audit.AuditLog` |
| Operational Record/Jira | Imported source records and durable transfer state | `ops.OperationalRecords`, `ops.JiraTransfers`, `ops.WorkflowHistory` |
| Notification | Notification dispatch records | `NotificationLog` |
| Compliance (Phase 5+) | Local admin expectations | `ExpectedLocalAdmins` |
| Analysis (Phase 6+) | Rule findings | `AnalysisRuleResults` |
| AI Audit (Phase 7+) | LLM interaction audit | `audit.AiAuditLog` |

## Core Entities (Domain Layer)

Current implementation note: `SecureOps.Domain` includes the Operational Record/Jira workflow aggregate and fail-closed classification/state enums. Its complete state transitions and persistence contract are documented in `docs/22-operational-record-jira-workflow.md` and `sql/schema/002-operational-record-jira-workflow.sql`.

Phase 1A IdentityLookup does not persist directory profiles. It resolves one exact account through a short-lived bounded cache and records only the privileged-read audit trail. `security.Users` stores the opaque authenticated corporate principal, authentication source, application access status, and timestamps; roles are separate assignments. Directory lookup responses are never copied into access tables.

```csharp
namespace SecureOps.Domain.Alerting;

public sealed class Alert
{
    public Guid Id { get; init; }
    public required string ExternalId { get; init; }      // upstream monitoring-source ID, e.g., SolarWinds
    public string? TuruncuhatEvtId { get; init; }         // operational EVT ID, if known
    public required string ServerName { get; init; }
    public Guid? ServerId { get; init; }                  // resolved server FK
    public required AlertType Type { get; init; }
    public required AlertSeverity Severity { get; init; }
    public required string Message { get; init; }
    public required DateTimeOffset OccurredAt { get; init; }
    public DateTimeOffset ReceivedAt { get; init; }
    public AlertStatus Status { get; private set; }
    public Guid? AssignedToUserId { get; private set; }
    public DateTimeOffset? AcknowledgedAt { get; private set; }
    public DateTimeOffset? ResolvedAt { get; private set; }

    // Methods: Acknowledge, Resolve, Reassign — all raise AlertEvents.
}

public enum AlertType
{
    Disk, Cpu, Memory, IisSite, IisAppPool, WindowsService, EventLog, Other
}

public enum AlertSeverity { Info, Warning, High, Critical }
public enum AlertStatus { Received, InProgress, Acknowledged, Resolved, Suppressed }
```

```csharp
namespace SecureOps.Domain.Diagnostic;

public sealed class DiagnosticJob
{
    public Guid Id { get; init; }
    public Guid AlertId { get; init; }
    public required string ModuleName { get; init; }   // e.g., "DiskDiagnostic"
    public required string TargetServer { get; init; }
    public DiagnosticJobStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public string? FailureReason { get; private set; }

    // Methods: MarkStarted, MarkCompleted, MarkFailed.
}

public enum DiagnosticJobStatus { Pending, Running, Completed, Failed, Cancelled }

public sealed class DiagnosticResult
{
    public Guid Id { get; init; }
    public Guid DiagnosticJobId { get; init; }
    public required string Schema { get; init; }       // e.g., "disk-diagnostic-v1"
    public required string ResultJson { get; init; }   // structured PowerShell output
    public required DateTimeOffset GeneratedAt { get; init; }
    public required Severity InferredSeverity { get; init; }
    public string? Summary { get; init; }              // short text for UI
}
```

## SQL Server Schema

DDL lives in `sql/schema/`. Below is the canonical MVP schema (Phase 1 deliverable).

### Schema: dbo

```sql
CREATE TABLE dbo.Servers (
    Id              UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
    Name            NVARCHAR(255) NOT NULL UNIQUE,
    Environment     NVARCHAR(50) NOT NULL,              -- 'Production', 'Test', 'Staging'
    Criticality     NVARCHAR(20) NOT NULL,              -- 'Low', 'Medium', 'High', 'Critical'
    InPilot         BIT NOT NULL DEFAULT 0,
    OwnerEmail      NVARCHAR(255) NULL,
    CreatedAt       DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    Notes           NVARCHAR(MAX) NULL
);

CREATE INDEX IX_Servers_InPilot ON dbo.Servers(InPilot) WHERE InPilot = 1;
```

```sql
CREATE TABLE dbo.Alerts (
    Id              UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
    ExternalId      NVARCHAR(255) NOT NULL,
    TuruncuhatEvtId NVARCHAR(100) NULL,
    ServerName      NVARCHAR(255) NOT NULL,
    ServerId        UNIQUEIDENTIFIER NULL REFERENCES dbo.Servers(Id),
    AlertType       NVARCHAR(50) NOT NULL,
    Severity        NVARCHAR(20) NOT NULL,
    Message         NVARCHAR(MAX) NOT NULL,
    OccurredAt      DATETIMEOFFSET NOT NULL,
    ReceivedAt      DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    Status          NVARCHAR(30) NOT NULL DEFAULT 'Received',
    AssignedToUserId UNIQUEIDENTIFIER NULL,
    AcknowledgedAt  DATETIMEOFFSET NULL,
    ResolvedAt      DATETIMEOFFSET NULL,
    PayloadJson     NVARCHAR(MAX) NULL,                 -- raw normalized payload
    INDEX IX_Alerts_ServerName_OccurredAt (ServerName, OccurredAt DESC),
    INDEX IX_Alerts_Status (Status),
    INDEX IX_Alerts_Type_OccurredAt (AlertType, OccurredAt DESC)
);

CREATE UNIQUE INDEX UX_Alerts_TuruncuhatEvtId
ON dbo.Alerts(TuruncuhatEvtId)
WHERE TuruncuhatEvtId IS NOT NULL;
```

```sql
CREATE TABLE dbo.AlertEvents (
    Id              UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
    AlertId         UNIQUEIDENTIFIER NOT NULL REFERENCES dbo.Alerts(Id),
    EventType       NVARCHAR(50) NOT NULL,              -- 'Received','Acknowledged','Resolved',...
    OccurredAt      DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    Actor           NVARCHAR(255) NOT NULL,             -- userid or 'system'
    Notes           NVARCHAR(MAX) NULL,
    INDEX IX_AlertEvents_AlertId (AlertId, OccurredAt)
);
```

```sql
CREATE TABLE dbo.DiagnosticJobs (
    Id              UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
    AlertId         UNIQUEIDENTIFIER NOT NULL REFERENCES dbo.Alerts(Id),
    ModuleName      NVARCHAR(100) NOT NULL,
    TargetServer    NVARCHAR(255) NOT NULL,
    Status          NVARCHAR(30) NOT NULL DEFAULT 'Pending',
    CreatedAt       DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    StartedAt       DATETIMEOFFSET NULL,
    CompletedAt     DATETIMEOFFSET NULL,
    FailureReason   NVARCHAR(MAX) NULL,
    INDEX IX_DiagJobs_Status (Status),
    INDEX IX_DiagJobs_AlertId (AlertId)
);
```

```sql
CREATE TABLE dbo.DiagnosticResults (
    Id                  UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
    DiagnosticJobId     UNIQUEIDENTIFIER NOT NULL REFERENCES dbo.DiagnosticJobs(Id),
    Schema              NVARCHAR(100) NOT NULL,        -- e.g., 'disk-diagnostic-v1'
    ResultJson          NVARCHAR(MAX) NOT NULL,        -- structured output
    GeneratedAt         DATETIMEOFFSET NOT NULL,
    InferredSeverity    NVARCHAR(20) NOT NULL,
    Summary             NVARCHAR(MAX) NULL,
    INDEX IX_DiagResults_JobId (DiagnosticJobId)
);
```

```sql
CREATE TABLE dbo.Users (
    Id              UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
    SamAccountName  NVARCHAR(255) NOT NULL UNIQUE,
    DisplayName     NVARCHAR(255) NOT NULL,
    Email           NVARCHAR(255) NULL,
    FirstSeenAt     DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    LastSeenAt      DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET()
);
```

```sql
-- Roles are derived from AD groups; this table maps AD group names to role codes
CREATE TABLE dbo.RbacRoles (
    Id              INT IDENTITY PRIMARY KEY,
    RoleCode        NVARCHAR(50) NOT NULL UNIQUE,       -- 'Operator', 'TeamLead', 'Admin', 'Auditor'
    AdGroupName     NVARCHAR(255) NOT NULL UNIQUE,      -- 'CONTOSO\\SecureOps-Operators'
    Description     NVARCHAR(MAX) NULL,
    Enabled         BIT NOT NULL DEFAULT 1
);
```

```sql
CREATE TABLE dbo.NotificationLog (
    Id              UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
    AlertId         UNIQUEIDENTIFIER NULL REFERENCES dbo.Alerts(Id),
    Channel         NVARCHAR(50) NOT NULL,              -- 'Teams', 'Mail', 'Ticket'
    Recipient       NVARCHAR(255) NOT NULL,
    Subject         NVARCHAR(500) NULL,
    BodyPreview     NVARCHAR(1000) NULL,
    Status          NVARCHAR(30) NOT NULL,              -- 'Sent', 'Failed'
    AttemptedAt     DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    ErrorMessage    NVARCHAR(MAX) NULL,
    INDEX IX_NotifLog_AlertId (AlertId)
);
```

### Schema: audit (Append-Only)

```sql
CREATE SCHEMA audit;
GO

CREATE TABLE audit.AuditLog (
    Id              BIGINT IDENTITY(1,1) PRIMARY KEY,
    OccurredAt      DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    Actor           NVARCHAR(255) NOT NULL,             -- userid, 'system:api', 'system:worker'
    Action          NVARCHAR(100) NOT NULL,             -- 'AlertReceived', 'DiagnosticStarted', ...
    AlertId         UNIQUEIDENTIFIER NULL,
    ServerName      NVARCHAR(255) NULL,
    CorrelationId   NVARCHAR(100) NULL,
    DetailsJson     NVARCHAR(MAX) NULL,
    SourceIp        NVARCHAR(45) NULL,
    INDEX IX_Audit_OccurredAt (OccurredAt DESC),
    INDEX IX_Audit_Actor (Actor, OccurredAt DESC),
    INDEX IX_Audit_AlertId (AlertId),
    INDEX IX_Audit_Action (Action, OccurredAt DESC)
);
GO

-- Append-only enforcement: block UPDATE and DELETE
CREATE TRIGGER audit.tr_AuditLog_BlockUpdateDelete
ON audit.AuditLog
INSTEAD OF UPDATE, DELETE
AS
BEGIN
    THROW 51000, 'Audit log is append-only. UPDATE/DELETE are not permitted.', 1;
END;
GO
```

### Schema: compliance (Phase 5+)

```sql
CREATE TABLE dbo.ExpectedLocalAdmins (
    Id              UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
    ServerId        UNIQUEIDENTIFIER NOT NULL REFERENCES dbo.Servers(Id),
    PrincipalName   NVARCHAR(255) NOT NULL,
    Justification   NVARCHAR(MAX) NULL,
    CreatedAt       DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    INDEX IX_ExpectedAdmins_ServerId (ServerId)
);
```

### Schema: analysis (Phase 6+)

```sql
CREATE TABLE dbo.AnalysisRuleResults (
    Id              UNIQUEIDENTIFIER NOT NULL PRIMARY KEY DEFAULT NEWID(),
    RuleId          NVARCHAR(100) NOT NULL,
    Subject         NVARCHAR(255) NOT NULL,
    Summary         NVARCHAR(MAX) NOT NULL,
    DetailsJson     NVARCHAR(MAX) NULL,
    Severity        NVARCHAR(20) NOT NULL,
    DetectedAt      DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    DataAsOf        DATETIMEOFFSET NOT NULL,
    INDEX IX_Findings_RuleId (RuleId, DetectedAt DESC),
    INDEX IX_Findings_Subject (Subject, DetectedAt DESC)
);
```

### Schema: ai (Phase 7+)

```sql
CREATE TABLE audit.AiAuditLog (
    Id              BIGINT IDENTITY(1,1) PRIMARY KEY,
    OccurredAt      DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    Actor           NVARCHAR(255) NOT NULL,
    MaskedPrompt    NVARCHAR(MAX) NOT NULL,
    Response        NVARCHAR(MAX) NOT NULL,
    ModelName       NVARCHAR(100) NOT NULL,
    ModelVersion    NVARCHAR(50) NOT NULL,
    PromptTokens    INT NOT NULL,
    CompletionTokens INT NOT NULL,
    RelatedAlertId  UNIQUEIDENTIFIER NULL,
    INDEX IX_AiAudit_OccurredAt (OccurredAt DESC)
);

CREATE TRIGGER audit.tr_AiAuditLog_BlockUpdateDelete
ON audit.AiAuditLog
INSTEAD OF UPDATE, DELETE
AS
BEGIN
    THROW 51000, 'AI audit log is append-only.', 1;
END;
```

## Retention Policy

| Table | Retention | Mechanism |
|---|---|---|
| `audit.AuditLog` | 36 months minimum | Partition + archive job |
| `audit.AiAuditLog` | 36 months minimum | Partition + archive job |
| `dbo.AlertEvents` | 24 months | Cleanup job |
| `dbo.DiagnosticResults` | 12 months | Cleanup job |
| `dbo.NotificationLog` | 12 months | Cleanup job |
| `dbo.Alerts` | Indefinite (summary record) | None |

Cleanup jobs are Hangfire recurring jobs, scheduled monthly.

## ID Strategy

- **`Guid` (NEWID)** for all business entities. Allows generation without round-trip.
- **`bigint identity`** for audit tables (no need for global uniqueness, simpler indexing).
- `DateTimeOffset` everywhere (no naive datetime).

### External Alarm Identifiers

`Alert` deliberately carries two external identifiers:

- `ExternalId` is the upstream alarm-source identifier, expected to come from SolarWinds or another monitoring source.
- `TuruncuhatEvtId` is the Turuncuhat `EVT-XXXXX` identifier when that record exists or is available to SecureOps.

This two-ID model preserves the upstream technical origin while also tracking the operational record used by the organization. In the current workflow, the durable reference operators are most likely to use across acknowledgment, closure, audit, and handover is `TuruncuhatEvtId`, while `ExternalId` remains important for source-system correlation and intake idempotency.

## Data Access

Dapper with parameterized SQL for all persistence, including audit writes; the schema is the numbered scripts in
`sql/` (ADR-0001, amended 2026-10-01). There is no ORM model.
