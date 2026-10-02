# Combined Source Integration, 2026-10-02

Inputs: remote master `eb2143c` (PR #5), PR #4 `34059b9`, final PR #6 `54b228f`.
Integrated each source once with merge ancestry. Actual conflict: `src/SecureOps.Ui/README.md`
route table; preserved the updated Service Accounts route descriptions and PR #5's other route rows.
No superseded bundles were reapplied. Toolchain, access concurrency and previous evidence remain intact.

Directory search fixes: require all given-name words in multi-part queries; use independently combined,
bounded Turkish/ASCII spelling alternatives in the real LDAP filter and the same comparison in the mock.
Prefix-only semantics, 3 letters/64 characters/4 tokens/10 results, capability and scope checks, minimal
attributes, RFC 4515 escaping, provider size/time limits and fail-closed audit remain. Excessive spelling
expansion fails validation at 256 alternatives. Audit failure at Requested/Completed/Failed stages is
tested with synthetic SQL triggers; no result is returned when audit persistence fails.

SQL inventory: 001-026. Number 026 promotes retained SA-002 DDL after verified 025, with both tables
and protective triggers in one transaction. Replay is refused. Separate SA-002 API grants permit
SELECT/INSERT/UPDATE on AccountUsages/TeamRoles only; no new Worker permission or role assignment.
Harnesses and source-bound packaging selection cover the full dependency closure. Role probes use
SQL execution tokens in fresh LocalDB databases, not corporate principal acceptance.

Review packaging requires a clean exact verified remote master, tested product-input equality and
source-bound SQL review with hashes. Numbered release branch/source guards remain unchanged.

## Local Windows Verification

Pinned SDK 9.0.317, C# 12, net8.0. Release solution build: zero warnings/errors. Full repository
format verification passed after separate commit `1487107`; token/literal equivalence was checked
for all fourteen formatted paths. No general quality rule or product gate was waived.

- Unit: 1,671 passed, zero failed/skipped; 686 UI/render cases are a subset, not an extra total.
- Integration with isolated Service Accounts SQL enabled: 328 passed, zero failed, 61 opt-in skips;
  45 Service Accounts cases are included. Expanded API/Worker role probes passed separately (2/2),
  including both 026 tables, positive SELECT/UPDATE and denied DELETE/ALTER/CONTROL.
- SQL selection/guard suite: 20 passed. Fresh 001-026 install, upgrade of retained 001-023 data,
  missing prerequisite refusals, injected 025/026 rollback and replay refusals passed in LocalDB.
- OpenAPI regenerated with the supported test switch from combined source; snapshot unchanged,
  compatibility test passed. Dapper, pinned SDK settings and access concurrency are retained.
- Real loopback API/UI: Resources journey passed; Service Accounts 13-step journey passed with
  persisted SQL role preview/apply/assignment, scope, import/replay, work/verification/closure,
  reminders without sending and immutable weekly XLSX/PDF exports. Monthly/custom report and
  snapshot-comparison journeys also passed; a later write left stored payloads unchanged.
- New directory journey: both capabilities independently enforced through HTTP, bounded queries,
  Turkish/ASCII matching, same names, scope-safe/ambiguous links and real 429 confirmed. UI usage
  entry and an explicit recommendation-based request passed; a recommendation created no work.
  Completed directory steps were reused when resuming the rule step, not rerun.

Evidence: `C:\SecureOpsBuild\validation\pr4-pr6-integration-20261002` (private, not packaged).
Unit/integration runtime product inputs were checked at `ee15c36`; subsequent changes are
documentation/browser/role-probe tests only. Original failures are retained: Debug/Release test
selection, pre-fix syntax/migration-count failures, audit-trigger cross-test deadlocks, host fixture
validation and obsolete browser selectors/assertions. The fault-probe test now uses the existing
nonparallel SQL collection; intentional in-test concurrency remains exercised. Unchanged Resources
SQL evidence (49 passed) is reused from `pr4-resources-20261002`, not added to new totals.
Mobile/desktop screenshots and DPR2 emulation are local evidence, not native-zoom acceptance.

Rollback: stop module writes under an approved target change, retain 026 tables, audit and new rows,
and restore matched older binaries only after reviewing serializer/snapshot compatibility. Do not
drop tables, replay installed migrations, disable triggers or replace server-owned configuration.
Candidate readyForInstallation remains false. Source merge is not target execution approval.

Unavailable/unexecuted acceptance: corporate OIDC/real AD, approved Turkish TEST names, desktop
Excel repair-prompt check, target principal permissions/ACLs and Worker/SCM execution. Local API/UI,
mock directory, persisted access, SQL-token probes and XLSX/PDF structure do not replace those gates.
No target SQL, deployment, live flags, Worker process or corporate write is authorized here.
