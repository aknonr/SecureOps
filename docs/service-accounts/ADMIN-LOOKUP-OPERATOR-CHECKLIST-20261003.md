# Admin / Lookup Operator Checklist

This is the single current checklist for this correction. Earlier exported
workflow/runbooks are reference history, not instructions to replay installed SQL
or activate integrations. Candidate readiness is local proof, not target approval.

1. **Recover first.** Operator restores the reviewed API read-only flags
   true/false/false (ReadOnlyIntegrationMode/ControlledTestWritesEnabled/
   SourceCloseEnabled), replacing existing entries only. Use the supplied
   `configuration/recovery.delta.xml` fragment as a review aid, never as a full
   web.config. Preserve OIDC, SQL/pool identity, UI API address and DPAPI key rings/
   ACLs. Keep completion, mail, Worker and scheduled reminders disabled. Confirm
   a fresh successful startup; do not relax a validator or trigger a write.
2. **Collect one prerequisite record.** Current installed API/UI ProductVersion
   and DLL SHA256; backup/window/rollback readiness; installed 024-026 inventory;
   effective nonsecret directory/module/audit/fence settings; affected user's
   refreshed AccessStatus, persisted roles/capabilities/access Version; the
   approved distinct scope-granting administrator, pilot user, exact operational
   capabilities/actions and Organization/Team scope. Do not send identities,
   tokens, profiles or connection strings. SQL identity/grants and application
   roles/scopes are separate; the pool account does not establish application Admin.
3. **Owner executes only reviewed 027.** Verify 019 protected/seeded Admin bundle,
   version/audit structures and 026 svcacct.TeamRoles. Back up first. Under separate
   SQL approval, SQLCMD working directory is `DBA/sql/migrations`; use the existing
   owner authentication and error-exit behavior (`-b -V 16`) for
   `027-admin-service-account-navigation.sql` only. Preserve installed 022-026;
   never replay 024-026. Verify bundle/audit/version outcome. Already-satisfied
   bundle rejects replay. Existing API grant scripts are references, not new
   role-membership commands. No separate DBA receipt is required.
4. **Install matched API then UI.** Under installation approval, verify both ZIP
   hashes/manifests and common candidate source SHA; back up current matched pair
   and restricted configuration, then use the existing config-preserving workflow.
   Never overwrite server-owned web.config/appsettings with publish templates.
   Do not mix new UI with old API or install a Worker. Check API health before UI.
5. **Refresh and enable nothing implicitly.** `/access/me -> Yenile` must show the
   approved persisted Admin and View/Administer for administration. Check the
   effective `ServiceAccounts:Provider=SqlServer` only against its existing module
   approval; if absent/Disabled, stop module acceptance and review that setting
   separately. `ServiceAccounts:Enabled` is not a supported switch. Preserve
   existing persistent audit and restricted SQL access; verify effective access
   through approved normal API operations after installation, not absent objects.
6. **Assign approved pilot access through supported pages.** A genuine Admin can
   open `/access/roles` and `/service-accounts/admin` without inventory scope.
   That independently authorized actor previews/applies only the approved finite
   operational bundle, assigns it to the approved different pilot through
   `/access/users/{id}` with reviewed versions and existing roles preserved, and
   grants only the approved Organization/Team scope. Self-grants remain denied.
   One module Admin is sufficient to scope a DIFFERENT pilot; it does not need
   to grant itself scope or invent another Admin. If the sole Admin is also the
   sole intended pilot, no supported self-scope path exists: hold those operational
   actions pending a separately approved independent authority; do not bypass.
7. **Target acceptance, not an inferred pass.** Refresh the pilot's access; perform
   only the previously approved scoped actions and record audit/outcomes. Verify
   ordinary and out-of-scope denial. Separately authorize read-only corporate AD
   acceptance on `/identity-lookup`: Tam hesap, then Ad / ad soyad with approved
   first/full-name queries (including Turkish/multipart matching). General lookup
   needs Identity.Lookup, not Service Accounts activation/scope; it shows no
   inventory links. Do not trigger Jira/source completion, mail or reminders.
8. **Rollback on mismatch.** Restore the previous matched API/UI pair and protected
   configuration while preserving keys, audit, scopes, additive 027 and increased
   versions. 027 failure is atomic. Any capability reversal is a separate audited
   owner amendment with increasing versions, not audit deletion/counter reset or
   routine full-database restore over subsequent work. Retain unknown outcomes.

Local proof: protected Admin navigation, finite-bundle/scoped synthetic workflows,
ordinary/out-of-scope/self-grant denials, both Mock lookup modes, atomic migration,
format/OpenAPI and matched payload/hash guards. Remaining target acceptance:
actual installed identity/configuration/user/scope/inventory, approved normal
SQL-backed pilot actions and corporate OIDC/AD. See the candidate's source-bound
verification record and [delivery delta](ADMIN-LOOKUP-DELIVERY-20261003.md).
