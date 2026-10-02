# Admin Access and General Directory Lookup

Owner-approved source correction, 2026-10-02. Codex owns this narrow authorization
and AD page/client change, including the necessary UI edits. No target SQL,
deployment, module/provider activation or corporate AD read is authorized here.

The [focused delivery delta](ADMIN-LOOKUP-DELIVERY-20261003.md) records the matched
source boundary, 027-only prerequisites/rollback and preserved runtime settings.

## Diagnosis Before Target Correction

Retain current installed API/UI ProductVersion and entry-DLL SHA256 from the
operator's existing read-only IIS inventory. The retained 2026-09-29 rc6.26
observation predates this installation; the existence of the 5264635 candidate
does not establish what is installed. No current target version or signed-in
persisted access response was available during source investigation.

The signed-in user opens `/access/me` and selects **Yenile**. Inspect the existing
`GET /api/v1/access/me` response privately if exact codes are needed; retain only
AccessStatus, role codes, capability codes and access Version, not identity,
tokens or cookies. Approved login/display name does not establish Admin.

The Service Accounts list's viewing-permission state occurs before module data
is loaded when its access snapshot cannot establish `ServiceAccounts.View`.
The source also mislabeled an unresolved access/API response as missing permission;
the corrected page renders that actual problem with an access refresh action.
The old message alone therefore does not establish missing persisted assignments. Missing scope
instead produces an authorized empty/no-scope view; a Disabled provider produces
503 NotConfigured after capability authorization. Runtime SQL failure is a
different unavailable response. Do not fix one layer by changing another.

Demonstrated source defect at 5264635: the catalog and persisted protected Admin
bundle omit module capabilities. SQL-backed access uses persisted role JSON;
changing the in-memory catalog alone cannot repair a target assignment.

## Reviewed Correction

The genuine protected Admin bundle gains only View/Administer. Numbered source
migration `027-admin-service-account-navigation.sql` requires reviewed 026,
preserves existing capabilities, increments role/affected access versions and
commits mandatory audit atomically under the existing administration lock.
Audit failure rolls everything back; a matching bundle rejects replay.
This is application authorization data, not SQL runtime grants. It changes no
provider flags, scope grants or ordinary-user assignments. The existing sealed
5264635 ZIPs do not contain this correction and are not recreated or relabeled.

After separate owner approval, the owner can review/apply the missing 027 bundle
amendment; there is no startup repair and no separate DBA receipt requirement.
The protected Admin bundle remains uneditable through the ordinary role editor.
If an immediate supplemental bundle is preferred on the installed version,
another authorized administrator can use `/access/roles` to preview/apply a
versioned bundle with exactly View/Administer, then `/access/users/{id}` to assign
it with reviewed role/user versions. Do not replace other held roles, expand an
owned bundle or self-escalate. All target authorization changes need explicit
owner approval and the supported audited command, not direct workflow SQL.

An administrator without module data scope can open administration and the
empty module, but cannot read ungranted account detail. Another authorized module
administrator uses `/service-accounts/admin` to grant only the approved
All/Organization/Team scope; self-grants remain rejected. Work, Assign, Verify,
Import and Report still require their explicit capabilities. Manual reminder
evaluation remains an existing Administer command and is not invoked in this task.
Worker and reminders remain excluded. Existing SQL rights and API SqlServer
activation must independently be approved; neither follows from an Admin role.

## General Lookup

`/identity-lookup` and `/directory/users` expose Tam hesap and Ad / ad soyad
modes. `POST /api/v1/identity/name-search` needs persisted Identity.Lookup only;
it works with the module disabled and uses the existing provider, query/parser,
Turkish/multipart prefix matching, LDAP escaping and shared rate limit.
Requested audit commits before provider access; completed/failed audit failures
return 503 with no results. Audit contains query hashes/counts, never names.
General results carry no inventory ids/links. Choosing an account starts exact
lookup without granting access. Existing module search retains scope-safe links.

## Operator Next Action

Capture current installed API/UI versions/hashes and refresh `/access/me` for
the affected signed-in user. Then classify the actual response:

- Pending/Disabled or absent Admin: an authorized different administrator reviews
  the user's existing persisted assignment; no automatic Admin assignment.
- Approved Admin but View/Administer absent: review the narrow bundle correction
  above. No SQL runtime grant or provider change can supply application capabilities.
- View present, module NotConfigured: review current API provider configuration
  against the separate activation approval; do not automatically enable it.
- View present and scope None: another authorized administrator supplies only the
  approved module scope; opening administration needs no self-grant.

Local tests demonstrate source behavior only; current target identity/assignments,
corporate OIDC/AD and target runtime SQL permissions remain separately unverified.
