# 15 — System Landscape

Single-page reference for the real systems SecureOps touches or must account for. This document is the repo's current "Rosetta Stone" for system names, roles, and unresolved integration questions.

| System | Role in current workflow | Integration type for SecureOps | Stakeholder team | Open questions |
|---|---|---|---|---|
| **SolarWinds** | Alarm source for CPU, disk, service, and similar events | Read later / indirect now | Monitoring platform team | Should SecureOps ever consume SolarWinds directly in production, or rely on Turuncuhat as the operational entry point? |
| **monthly.thy.com / HPE OpsBridge** | Event-detail viewer layer between SolarWinds and Turuncuhat | None yet | Monitoring platform team | Confirm product role and whether any SecureOps integration is needed. |
| **Turuncuhat** | Central ITSM + IVR system; creates `EVT-XXXXX`, opens PR/OR/OCO, sends mail/IVR, owns acknowledge/close workflow | Both likely, exact contract pending | Turuncuhat team | Webhook vs. API? Read-only vs. read-write? |
| **BeyondTrust** | PAM used by humans with LDAP credentials for privileged RDP/PowerShell access | Read later / Worker path pending | PAM / BeyondTrust team | Should Worker use BeyondTrust brokering, direct WinRM + JEA, or direct JEA with explicit acceptance? |
| **Dynatrace** | Existing APM platform | None yet | Application / observability stakeholders | Current WASAS use and relevance to SecureOps are unclear. |
| **vSphere** | VM management and future snapshot visibility | Read later | Virtualization / infrastructure team | Exact API path and pilot availability to be confirmed before Phase 5+/8. |
| **Confluence WASAS** | Team documentation source | None yet | WASAS team | Whether later read-only knowledge integration is useful remains open. |

## Workflow Summary

Current operator workflow:

1. SolarWinds raises the alarm.
2. monthly.thy.com / HPE OpsBridge exposes event detail.
3. Turuncuhat creates the EVT record and notifies the on-call engineer.
4. The engineer investigates through SolarWinds and/or BeyondTrust.
5. The engineer returns to Turuncuhat to close the EVT with status and action notes.

SecureOps must fit this existing flow rather than replace it. In particular:

- **Turuncuhat is the operational system of record.**
- **SolarWinds remains the upstream alarm source.**
- **The Worker privileged-access path is not yet decided.**

## Open Decisions

1. **Turuncuhat integration contract:** webhook vs. API, and read-only vs. read-write.
2. **Worker privileged-access path:** BeyondTrust-brokered sessions vs. direct WinRM + Kerberos + JEA vs. the current direct-JEA model with explicit stakeholder acceptance.
