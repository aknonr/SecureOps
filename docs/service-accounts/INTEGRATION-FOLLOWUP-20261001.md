# Combined Windows Follow-up Evidence

Supporting evidence only. Requirements stay in [integrated-test-activation.md](../integrated-test-activation.md).

## Identity and Bounded Review

- Branch: `feature/service-accounts-pinned-integration-20260929`.
- Baseline: `12027da06898938a5851f359cc7ddd8526997928` (b4-only evidence).
- Bundle SHA-256: `31B0E3899BA0BE77E9B3F2FF9D39534907CA7A767F6D27A57D19FE9CAC93A99B`.
- Verified ancestor: `b4fdf8d439990032fb5f4c21486030bdb903a27c` -> `7e227edda206cf65ac4d36991358aa0d0c677ba6`.
- Five follow-up commits were merged ONCE at `4b9b41e959c9ba8a7c5d03c7563bbfb644aa1676`; retained baseline documentation was preserved.
- Final tested source: `a457a33d4fb34a6c21e675b81eed5f23b6ad470f`.
  API/UI/Worker entry ProductVersion each equals `0.1.0+a457a33d4fb34a6c21e675b81eed5f23b6ad470f`.
  No package, installation or pilot approval follows from these local builds.
- Review slices: closure domain/infrastructure guards; existing UI validation/picker;
  scenario-4/report/export tests; restricted-role tests; fixture seeding;
  scope-grant concurrency; supporting documentation. No SQL candidate, Worker
  composition, platform access, existing SDM UI or OpenAPI contract was replaced.
  The full integration run includes OpenAPI snapshot verification; no schema/route delta.

## Defects and Retained Failures

Review-tagged Closure is fixed: only Deletion/GmsaConversion may satisfy verified
account closure. New report/update paths reject other Closure kinds. Imported
historical kinds remain intact but Review/PasswordChange etc. do not close accounts
or inflate verified-closure reporting. Scenario 4 separates eight password plans
from two review requests, including performed/verified intermediate review outcomes.

At `59abe3d2c9c38e45bc4eeee06babee8eb9c152c0`, first module run: 37 passed,
1 failed. Local SQL deadlock graph identifies raw synthetic Users INSERT against
the platform SERIALIZABLE role-impact scan. Fixture seeding now takes the existing
`SecureOps.Access.Administration.v1` transaction lock (`bf711955`). This does NOT
prove a production EnsureUser defect or explain the older lost first-run failures.

At `bf711955d6ac1ef40aa4a9cca341cae2cdb224c4`, full integration: 366 passed,
9 failed, 7 skipped. Eight failures were unrelated OCO opt-ins selected by the
generic resource DB environment variable: six require an OcoSource DB; two require
an Oco draft DB. The ninth was real SQL 1205 between TWO repository CreateGrant
transactions converting ScopeGrants PK RangeS-S locks to RangeI-N.
The duplicate read now uses `UPDLOCK, HOLDLOCK` (`ecdd252`), retaining SERIALIZABLE,
audit atomicity and duplicate rejection. The new concurrent test commits eight
distinct grants and only one of two identical grants, with exactly nine audits.
No retry, sleep, suite serialization or weakened assertion was added.
The intermediate `ecdd252` build failed IDE0007 in the new test; `a457a33` fixes
only its inferred-type syntax. All failed outputs/DBs remain retained.

## Final Windows Checks at a457a33

Evidence root: `C:\SecureOpsBuild\validation\sa-followup-20260930`.
Sibling `sa-followup-*-20261001.log` files contain build/harness/test output.

| Check | Actual result | Evidence |
|---|---|---|
| Release no-incremental solution build | 0 warnings/errors | `sa-followup-build-verified-20261001.log` |
| Full unit gate, once at final source | 1575 passed, 0 failed/skipped | `unit-verified.trx` |
| Full normal integration with module SQL, once at final source | 322 passed, 0 failed, 61 opt-ins skipped | `integration-verified.trx` |
| Module subset INCLUDED in 322 | 39 passed: 36 SQL/persisted-composition/role tests + 3 HTTP composition tests | Same TRX |
| Fresh module installation and replay guard | 001-024 + unnumbered SA candidate/roles; replay refused; no corporate role members | `sa-followup-db-repair-20261001.log`; `SecureOps_SaFollow1001C` |
| ResourceSql, separate fresh 001-024 upgrade fixture | 49 passed, 0 failed/skipped | `resource-sql-verified.trx`; `SecureOps_ResourcesV1_SaFollow1001C` |
| Safe SQL diagnostics | Only intentional audit failure 51091, state 1, class 16 | `verified-diagnostics.log` |

Counts are not summed: the 49 resource tests were skipped in the normal gate;
earlier focused passes and failed runs are not additional final acceptance totals.
Final normal gate sets only the module SQL connection; the resource connection is
set solely for the ResourceSql filter. OCO opt-in journeys were NOT run at final
source. Prior b4/Linux evidence is historical, not evidence for this identity.

Restricted API/Worker SQL tests use rollback-only synthetic no-login execution
tokens on the explicitly guarded LocalDB instance. Required verbs and forbidden
DDL/audit/history privileges are checked, including denied statements (SQL 229).
This is local role enforcement, NOT a separate Windows-account connection or
corporate normal-API permission observation. Persisted-access composition uses
the real access service/bundles and SQL scope; HTTP tests cover unauthenticated
challenge, platform-role denial and all endpoint policies. They do NOT prove an
allowed real-OIDC browser journey. No access-service capability wrapper was used.

## Open Gates

- Approved normal-auth TEST identities/bundles/scopes, allowed HTTP/UI pilot,
  separate restricted Windows runtime accounts, IIS and desktop Excel acceptance
  remain unexecuted; use [WINDOWS-ACCEPTANCE.md](WINDOWS-ACCEPTANCE.md).
- Older lost first-run errors remain unresolved; these captured deadlocks have
  specific fixes but cannot retroactively identify those old exceptions.
- No live flags, target SQL, Worker execution, corporate calls, mail, push or
  release packaging. In Use IU-05, Jira-only acceptance and Falcon remain separate.
- Preserved source/evidence/artifact locations: [workspace index](../build-workspace-index.md).
