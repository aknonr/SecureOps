# 000 — Project Context

## Systems around the platform

- **SolarWinds** — alarm source. **HPE OpsBridge / monthly.thy.com** — event-detail layer. **Turuncuhat** — central ITSM + IVR workflow. **Jira** — operational-record target (ADR-0009, ADR-0012). **BeyondTrust** — PAM (read-only correlation). Existing Ansible/AWX is not touched.
- The platform runs on top of these systems, never inside them; it does not modify their configuration. Current map and open questions: `docs/15-system-landscape.md`.

## Pilot scope

10–15 low-criticality Windows servers (prefer non-production), one or two alarm types first (Disk, Service), 4–6 weeks parallel run with human operators, then a management review. Phase status: `docs/02-roadmap.md` and `plans/`.

## Access model

Authentication only establishes who someone is. Access comes from the persisted path *principal → approval status → application role (bundle) → capability* (ADR-0010, ADR-0022). The API enforces capability policies (`SecureOps.Shared.Auth.Policies`, `Capabilities`); the UI only reflects them. Do not reintroduce AD-group or role-name checks in code.

## Why the constraints exist

Bus factor is 1 and the developer works part-time around shift duty. Documentation is the continuity plan, mainstream technology keeps maintenance possible, and small changes keep review possible. Every audit or reporting feature is framed as operational response evidence (SLA, incident verification), never as individual performance measurement.
