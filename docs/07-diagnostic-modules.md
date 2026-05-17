# 07 — Diagnostic Modules

The Worker runs **diagnostic modules** in response to alarms. Each module is a self-contained, read-only investigator for a specific alert type.

## Module Pattern

Modules implement `IDiagnosticModule`:

```csharp
public interface IDiagnosticModule
{
    string Name { get; }
    AlertType[] Handles { get; }

    Task<DiagnosticResultPayload> RunAsync(
        Alert alert,
        IPowerShellRunner powerShell,
        CancellationToken cancellationToken);
}
```

A `DiagnosticModuleRegistry` selects the module for an alert by matching `Handles`. Each module is registered in DI.

## Strategy Pattern

```csharp
public sealed class DiagnosticRunner : IDiagnosticRunner
{
    private readonly IDiagnosticModuleRegistry _registry;
    private readonly IPowerShellRunner _powerShell;
    private readonly IDiagnosticResultStore _store;
    private readonly IAuditWriter _audit;

    public async Task RunForAlertAsync(Guid alertId, CancellationToken ct)
    {
        var alert = await _alerts.GetAsync(alertId, ct);
        var module = _registry.GetFor(alert.Type);
        var result = await module.RunAsync(alert, _powerShell, ct);
        await _store.SaveAsync(alertId, module.Name, result, ct);
        await _audit.WriteAsync(new AuditEvent
        {
            Action = AuditAction.DiagnosticCompleted,
            AlertId = alertId,
            ServerName = alert.ServerName,
            Actor = "system:worker",
            Details = new { Module = module.Name }
        }, ct);
    }
}
```

## Standard Modules (Phase 1)

### 1. DiskDiagnosticModule

**Handles:** `AlertType.Disk`

**Outputs:**
- All drives: total/used/free GB and percentage.
- The flagged drive: top folders by size (configurable depth and count).
- Recent file growth (last 24h, configurable).

**PowerShell:**
- `Get-PSDrive`, `Get-Volume`
- `Get-ChildItem` with `Measure-Object` for folder sizes
- All read-only

**Result schema:** `disk-diagnostic-v1` (see `contracts/schemas/diagnostic-result.schema.json`)

```json
{
  "schema": "disk-diagnostic-v1",
  "serverName": "APPSRV-12",
  "generatedAt": "2026-06-15T14:23:25Z",
  "flaggedDrive": "D:",
  "drives": [
    { "name": "C:", "totalGb": 100, "freeGb": 35, "usedPercent": 65 },
    { "name": "D:", "totalGb": 500, "freeGb": 8, "usedPercent": 98 }
  ],
  "topFolders": [
    { "path": "D:\\Logs", "sizeGb": 220 },
    { "path": "D:\\Backup", "sizeGb": 180 }
  ],
  "summary": "Drive D: at 98% used. Top space: D:\\Logs (220 GB)."
}
```

### 2. CpuDiagnosticModule

**Handles:** `AlertType.Cpu`

**Outputs:**
- Current CPU utilization.
- Top N processes by CPU.
- Top N processes by handle count.
- Recent CPU history if Get-Counter is available.

**PowerShell:**
- `Get-Counter '\Processor(_Total)\% Processor Time' -SampleInterval 1 -MaxSamples 5`
- `Get-Process | Sort-Object CPU -Descending | Select-Object -First 10`

**Result schema:** `cpu-diagnostic-v1`

### 3. MemoryDiagnosticModule

**Handles:** `AlertType.Memory`

**Outputs:**
- Total/available physical memory.
- Top N processes by working set.
- Memory pressure indicators (paging activity if accessible).

**PowerShell:**
- `Get-CimInstance Win32_OperatingSystem` for memory totals
- `Get-Process | Sort-Object WorkingSet64 -Descending | Select-Object -First 10`

**Result schema:** `memory-diagnostic-v1`

### 4. IisDiagnosticModule

**Handles:** `AlertType.IisSite`, `AlertType.IisAppPool`

**Outputs:**
- Site state.
- App pool state.
- Pool identity and recycle settings (read-only).
- Recent IIS log line count (no log content reading).
- Recent W3SVC events (last 1 hour, error/warning only).

**PowerShell:**
- `Get-Website`, `Get-WebApplication`
- `Get-WebAppPoolState`
- `Get-WebConfigurationProperty`
- `Get-WinEvent -LogName 'System' -FilterHashtable @{ProviderName='Microsoft-Windows-WAS'}`

**Result schema:** `iis-diagnostic-v1`

### 5. ServiceDiagnosticModule

**Handles:** `AlertType.WindowsService`

**Outputs:**
- Service state, startup type, log on as.
- Dependencies and dependent services.
- Recent service-related events from the System log.

**PowerShell:**
- `Get-Service`, `Get-CimInstance Win32_Service`
- `Get-WinEvent -LogName System -FilterHashtable @{Id=7034,7036,7040}`

**Result schema:** `service-diagnostic-v1`

### 6. EventLogDiagnosticModule

**Handles:** `AlertType.EventLog`

**Outputs:**
- Recent events from the specified log (last N hours, configurable).
- Filtered by event ID range or provider as specified in alert metadata.
- Limited to first 50 events to avoid huge payloads.
- Each event: time, level, provider, ID, short message.

**PowerShell:**
- `Get-WinEvent -LogName <name> -FilterHashtable <filter>`

**Result schema:** `eventlog-diagnostic-v1`

## Severity Inference

Each module includes a severity inference for the result (separate from the alert's original severity):

| Module | Critical condition | High | Warning | Info |
|---|---|---|---|---|
| Disk | Free < 5% or < 1 GB | Free < 10% or < 5 GB | Free < 20% | Otherwise |
| CPU | Sustained > 95% | > 85% | > 70% | Otherwise |
| Memory | Available < 5% | < 10% | < 20% | Otherwise |
| IIS | Site/Pool stopped | Pool recycling repeatedly | Configuration warnings | Otherwise |
| Service | Stopped + Automatic startup | Stopped + Manual startup | Recovery attempted | Otherwise |
| EventLog | Critical events present | Multiple Error events | Multiple Warning events | Otherwise |

Inferred severity drives UI presentation and may differ from the upstream alarm-source severity or the Turuncuhat EVT severity exposed to SecureOps.

## Module Configuration

Each module reads `appsettings.json`:

```json
{
  "Diagnostic": {
    "Disk": {
      "TopFoldersDepth": 2,
      "TopFoldersCount": 10,
      "RecentFileWindowHours": 24
    },
    "Cpu": {
      "TopProcessesCount": 10,
      "SampleIntervalSeconds": 1,
      "SampleCount": 5
    },
    "Memory": {
      "TopProcessesCount": 10
    },
    "Iis": {
      "RecentEventWindowMinutes": 60
    },
    "Service": {
      "RecentEventWindowMinutes": 60
    },
    "EventLog": {
      "MaxEventsReturned": 50,
      "DefaultWindowHours": 1
    }
  }
}
```

## Timeouts

Each module has a hard timeout. Defaults:

| Module | Timeout |
|---|---|
| Disk | 60s (folder traversal can be slow) |
| CPU | 15s |
| Memory | 10s |
| IIS | 20s |
| Service | 15s |
| EventLog | 30s |

Beyond timeout, the module returns a partial result with `"truncated": true`.

## Testing

For each module:

- **Unit tests:** mock `IPowerShellRunner`, verify command construction and result parsing.
- **Integration tests:** use the `MockPowerShellRunner` with canned outputs from real (anonymized) data.
- **Smoke tests:** run against a known pilot server in a separate test project.

## Adding a New Module

Steps:

1. Add the `AlertType` enum value if needed.
2. Create `<Name>DiagnosticModule.cs` in `SecureOps.Infrastructure.Diagnostic`.
3. Implement `IDiagnosticModule`.
4. Register in DI.
5. Define a result schema in `contracts/schemas/`.
6. Update the JEA whitelist if new cmdlets are required (this requires an ADR).
7. Add unit tests + sample fixtures.
8. Update this document.

## Reference

- `contracts/schemas/diagnostic-result.schema.json`
- `.cursor/rules/030-worker-service-rules.mdc`
- `.cursor/rules/040-automation-ansible-powershell-rules.mdc`
