# scripts/jea/proposed/

**Proposed JEA artifacts. Do not register, install or copy them to any server.** They exist so that the design can be
reviewed (ADR-0024) and unit-tested with synthetic data. Deployment needs: ADR-0024 accepted, Bilgi Güvenliği approval
of the role capability recorded in the ADR, a pilot server list signed off by the server owners, and the Worker job.

| Folder | Purpose |
|---|---|
| `SecureOps.ServiceAccountUsage/` | Read-only discovery of where service accounts run (Windows services, scheduled tasks, IIS identities without passwords) and the post-conversion gMSA check. One visible function: `Get-SecureOpsAccountUsage`. Output contract: `contracts/schemas/service-account-usage.schema.json`. |

Tests: `tests/SecureOps.Tests.Unit/ServiceAccounts/ServiceAccountUsageModuleTests.cs`.
