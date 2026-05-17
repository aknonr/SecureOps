# ADR-0007 — IIS Hosting Model

**Status:** Accepted
**Date:** 2026-05
**Decision makers:** Project owner

## Context

The system has three runtime components: API, UI, and Worker. They must be hosted on Windows Server in the existing CONTOSO environment.

Options for hosting:

1. IIS in-process (chosen for API + UI).
2. Kestrel as a standalone Windows Service.
3. Docker containers (no on-prem Windows Container infrastructure).
4. Self-contained executable on Windows Service.

## Decision

- **API:** Hosted on **IIS in-process**.
- **UI (Blazor Server):** Hosted on **IIS in-process**, separate site.
- **Worker:** Registered as a **Windows Service**, not on IIS.
- **SQL Server:** On the existing enterprise database server.

For pilot scale, all three components can run on the same physical or virtual server. For production, separate the Worker from the IIS host.

## Alternatives Considered

### Kestrel + Windows Service for the API

Considered. Rejected for MVP because:
- IIS is already used widely at CONTOSO.
- Windows Authentication integration is more straightforward in IIS.
- Application pool recycling provides operational benefits.
- The team has IIS skills.

### Containers (Linux or Windows)

Rejected:
- No existing container orchestration on-prem.
- Windows Server containers add operational complexity.
- Out of scope for MVP.

### Self-contained .exe directly

Rejected for the API:
- Loses IIS benefits (recycling, request logging, easy deployment).
- Kept for the Worker because it has no HTTP surface.

### Same process for API + UI + Worker

Rejected:
- Mixes lifetime concerns.
- API restart kills in-flight Worker jobs.
- Hangfire prefers a long-running process; IIS app pools recycle.

## Consequences

### Positive

- Leverages existing IIS skill and tooling.
- Native Windows Auth.
- Standard deployment patterns.
- Operational visibility through IIS logs and metrics.

### Negative

- IIS app pool recycling can interrupt API requests (mitigated by short request times).
- Two separate hosting models to operate (IIS + Windows Service).
- Worker deployment is manual (sc create / `dotnet publish` + service install).

### Neutral

- Logs go to file + SQL via Serilog regardless of host.

## Implementation Notes

- API and UI as separate IIS sites or applications under one site.
- HTTPS only; HSTS enabled; TLS 1.2 minimum.
- App pool identity: `CONTOSO\svc-secureops`.
- Worker service identity: same `CONTOSO\svc-secureops`.
- IIS application initialization configured for warmup.
- ASP.NET Core Module v2 for in-process hosting.

## Deployment Topology Examples

### Pilot (Single Server)

```
[Single Windows Server]
├── IIS
│   ├── Site 1: SecureOps API (port 443, app pool 1)
│   └── Site 2: SecureOps UI (port 443, app pool 2, different host header)
├── Windows Service: SecureOps Worker
└── (SQL Server is on a different existing server)
```

### Production (Recommended)

```
[Web Server (IIS)]                [Worker Server]                 [SQL Server]
├── SecureOps API                 ├── SecureOps Worker            └── SecureOps DB
└── SecureOps UI                  └── Hangfire Server
```

## References

- `docs/03-architecture.md`
- `ADR-0001-technology-stack.md`
