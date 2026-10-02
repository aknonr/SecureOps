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

Rollback: stop module writes under an approved target change, retain 026 tables, audit and new rows,
and restore matched older binaries only after reviewing serializer/snapshot compatibility. Do not
drop tables, replay installed migrations, disable triggers or replace server-owned configuration.
Candidate readyForInstallation remains false. Source merge is not target execution approval.

Unavailable/unexecuted acceptance: corporate OIDC/real AD, approved Turkish TEST names, desktop
Excel repair-prompt check, target principal permissions/ACLs and Worker/SCM execution. Local API/UI,
mock directory, persisted access, SQL-token probes and XLSX/PDF structure do not replace those gates.
No target SQL, deployment, live flags, Worker process or corporate write is authorized here.
