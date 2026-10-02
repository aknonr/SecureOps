# Admin and General Lookup Delivery Delta

Focused source delivery, 2026-10-03. Source publication is approved; source merge,
installation, target SQL and corporate permission changes remain separate actions.
The operator is recovering the installed API independently. Missing target
observations do not block this source PR. Jira activation, In Use transport and
Worker work are excluded.

## Matched Source Identity

Base: `5264635c2b38a75a1203505cb8ae8f3b4ba3bf29`.
Branch: `fix/admin-service-accounts-ad-lookup-20261002`.
The PR head SHA recorded in the publication record identifies BOTH the corrected
API and UI sources; after an approved merge, both installation artifacts must be
built from the same resulting verified merge SHA. Do not mix this UI with the
5264635 API: the new name-search route is additive and absent from that API.
Private diagnostic DLL hashes from the earlier uncommitted build are not a
committed delivery identity. The existing matched 5264635 ZIPs and candidate
record are unchanged and do not contain this correction. No new package is
created by source publication; a later guarded matched build needs its own
artifact hashes/source identity before installation.

Changes:

- Genuine protected Admin gains only ServiceAccounts.View/Administer. Ordinary
  roles, explicit module scopes, self-grant prohibition, audit and operational
  capability requirements are preserved. Migration 027 updates the persisted
  bundle; the catalog alone cannot repair SQL-backed access.
- Unresolved access is displayed as its actual API/access problem, not falsely
  labelled missing viewing permission.
- General exact-account and bounded first/full-name lookup share the existing
  provider/query protections. General name search uses Identity.Lookup alone,
  with no inventory identifiers or module dependency. Module search retains
  scope-safe links. Name-search audit requires durable sink acknowledgement
  before provider access and before releasing results, even when ordinary audit
  uses its existing queue. No global audit-queue setting change is needed.

## Verification Reused

Product/test/SQL/browser sources are unchanged since the completed local run;
only this delivery note and its documentation link are subsequent additions.
Windows SDK 9.0.317, C# 12, net8.0: Release solution build 0 warnings/errors;
repository-wide format and OpenAPI snapshot passed; unit 1675 passed; integration
343 passed with 61 opt-in skips and zero failures. The focused 46 module cases
overlap that integration result, not an additional total. Separate migration
harness: 17 assertions, including atomic audit-failure rollback and replay
rejection. Actual combined API/UI synthetic browser journey: 5/5, administrator
navigation, ordinary/out-of-scope and self-grant denial, both lookup modes,
Turkish/multipart matching and desktop/mobile layout. No corporate acceptance
is claimed. Publication adds diff, documentation-link and outgoing-content checks,
not a repeat product suite.

Delivery completion adds only release scripts/configuration fragment/checklist,
not application sources. The explicit 027-only selector passed 15 guard checks.
Additional real loopback API proof used a fresh isolated SQL database with exactly
ONE persisted Admin and demo compatibility disabled: 12 supported authorization
checks passed. That unscoped Admin approved the distinct ordinary user, previewed/
applied a finite View/Assign/Work/Report/Identity.Lookup bundle, preserved ReadOnly
on assignment, granted Organization scope, and the pilot created/edited an
account, created/updated work and obtained the current-week report. Ordinary,
out-of-scope, self-grant and implicit-Admin-action denials, scope revocation and
durable audit were checked. No second Admin, All scope or Verify/Import/Administer
capability was supplied to the pilot. A first harness report call omitted its
weekStart query; the supported UI-style dated request passed. Failed local fixture/
harness evidence is retained, not promoted as acceptance. This establishes a
viable initial path for a DIFFERENT pilot, not self-scope for a sole Admin/pilot.

The matched master candidate uses the tracked `-FromVerifiedMaster -ApiUiOnly
-UpgradeFromInstalled026` workflow with source-bound reviewed hashes and all
existing clean-source/payload/dependency/version/destination guards. Its single
[operator checklist](ADMIN-LOOKUP-OPERATOR-CHECKLIST-20261003.md) is the current entry.

## Migration 027 Only

Deliver together:

- [SQLCMD entry](../../sql/migrations/027-admin-service-account-navigation.sql)
- [Transactional implementation](../../sql/schema/027-admin-service-account-navigation.sql)

Before separately approved owner execution, verify installed 024, 025 and 026
inventory using existing installation records/object observations. Do NOT replay
installed 024-026 or reinstall 022/023. If a prerequisite is missing, stop 027 and
resolve that distinct upgrade gap; this PR does not authorize replay.

027 requires reviewed role bundles (019): security.Roles with CapabilitiesJson
and Version, security.Users.AccessVersion, security.RoleAssignments.RevokedAt,
the seeded/protected Admin row with valid capability JSON, append-only
audit.AuditLog, and reviewed 026 including svcacct.TeamRoles. Existing module
tables/API runtime grants must already be present for module operations. No
new table, runtime SQL role/member assignment or grant script is introduced by
027. The owner migration connection needs the reviewed permission to read
assignments, update role/user versions, acquire the existing transaction lock
and insert audit; do not grant those migration rights to the API runtime account
merely to run this script.

Under explicit target-change approval, use SQLCMD from `sql/migrations`, with
the owner-selected existing SQL authentication method, error exit enabled
(`-b -V 16`) and input `027-admin-service-account-navigation.sql`. Do not put
credentials in a command/history or run the folder's entire migration sequence.
This task executes no such target command and requires no separate DBA receipt.

The script holds SecureOps.Access.Administration.v1 exclusively in its transaction,
appends only missing View/Administer, increments the Admin role version and
active-assignment users' access versions, and inserts AccessRoleDefinitionChanged
with correlation `migration:027:admin-service-account-navigation`. Failure rolls
back all changes. If both capabilities already exist, it rejects replay: compare
the existing bundle and audit instead of rerunning or resetting versions.

### Backup and Rollback

Before an approved installation, retain the current matched API/UI payload hashes,
restricted configuration backups, normal verified database backup and a private
snapshot of the Admin capabilities/version and affected access versions. Preserve
OIDC, SQL identity, DPAPI key rings/ACLs and all historical audit/evidence.

Deploy the matched API before the matched UI in the operator's approved window;
do not allow UI use of the new route until the matching API is healthy. The
owner-approved 027 must be present before expecting the genuine persisted Admin
bundle correction. Scope assignment is neither performed nor implied by 027.

If 027 fails, its transaction rolls back automatically: inspect the exact error,
do not reset counters or delete audit, and do not replay earlier migrations.
For application rollback, restore the previous MATCHED API/UI pair and protected
configuration backups; preserve key rings. Leave additive 027 capabilities,
increased versions, scope data and audit intact. The previous 5264635 API already
recognizes the capability names. Application rollback is not authorization
revocation. If the owner decides to revoke the bundle amendment, that is a
separate reviewed, audited protected-role amendment under the administration
lock, preserving unrelated capabilities and INCREMENTING role/affected user
versions again. Never decrement versions, delete audit/scope data or restore a
whole database over subsequent operations as a routine application rollback.

## Minimal Runtime Settings

| Layer | Required setting/evidence | Delivery delta |
|---|---|---|
| API recovery fences | OperationalRecords:ReadOnlyIntegrationMode=true, ControlledTestWritesEnabled=false, SourceCloseEnabled=false | Operator handles separately; preserve recovered read-only state, no Jira/source activation |
| API module persistence | ServiceAccounts:Provider=SqlServer only for the independently approved/installed module; existing SecureOpsDb connection and restricted API runtime grants | Preserve existing approved values; if disabled/unconfigured, review separately rather than silently enable |
| API directory provider | Existing approved IdentityLookup:Provider and ActiveDirectory DomainName/bounds if selected | Preserve; no new name-search enable flag or live AD query is required by publication |
| API durable audit | Existing approved persistent Audit:Provider and FailClosed=true | Preserve; new route uses DirectAuditWriter, no queue-wide change |
| API/UI authentication and hosting | Existing OIDC/session configuration, UI-to-API base address, SQL app-pool identity, DataProtection mode/application/key-ring paths and ACLs | Preserve exactly; do not import configuration from a publish template or Markdown copy |
| Completion/schedulers | InUseCompletion remains disabled; ServiceAccounts:Reminders:Enabled=false; no Worker start | Preserve excluded pilot boundaries; no scheduled-reminder, mail or transport work |

ServiceAccounts:Enabled is computed, not a supported activation switch. General
name lookup requires no Service Accounts provider/scope activation. Server
settings/SQL permissions cannot supply a user's application capabilities.

## Application Authorization and Target Evidence

After separate API recovery, the affected signed-in user opens `/access/me` and
selects **Yenile**. Retain only AccessStatus, persisted role/capability codes and
access Version; no profile, token or cookie. The pool service account, display
name, successful OIDC login and incoming role-evidence claim are not proof of
application Admin. Approved genuine Admin gets navigation/admin capability,
not all operational actions or all inventory scope.

Another authorized module administrator supplies only an approved All,
Organization or Team scope through `/service-accounts/admin`; no self-grant.
Scope None permits administration/empty list, not ungranted account detail.
Work/Assign/Verify/Import/Report remain explicit capabilities. A supplemental
bundle correction, if preferred before 027, uses another authorized administrator's
supported role-preview/apply and user-assignment workflow with preserved roles
and reviewed versions; no direct target permission write is performed here.

Remaining target observations, separate from source publication:

1. Current installed API/UI ProductVersion and entry-DLL SHA256, plus the eventual
   matched artifact/source identity. Owner-confirmed deployment is not the exact
   installed source/hash observation.
2. Affected user's refreshed AccessStatus, actual persisted role/capability codes
   and access Version; explicit module scope kind/approved binding when inventory
   access is expected. Do not upload identities or raw /access/me profile data.
3. Effective nonsecret provider/fence settings and their configuration layer:
   ServiceAccounts provider, directory provider and recovered read-only/completion/
   reminder booleans; successful current startup. Missing installed observations
   do not block this PR.
4. Existing 024-026 inventory, reviewed 027 bundle/audit outcome after separate
   owner execution, and effective restricted API access through normal approved
   operations. The pool identity/Integrated Security configuration is inferred
   SQL identity, not an observed SQL principal or proof about impersonation.
5. Corporate OIDC/AD and normal installed API/UI acceptance of both lookup modes
   under a separately authorized read-only acceptance check. No such live query
   is executed by source publication. Desktop Excel and Worker acceptance stay
   outside this correction, not invented passes or unrelated source blockers.
