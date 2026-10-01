# Platform Access, Concurrency, and Release Safety

## Current Administration, 2026-09-17

Migration 019 persists role code/ID, display name, purpose, registered capability
bundle, version and protected status without changing the nine-role baseline below.
Migration 020 makes existing OCO source/preparation rights explicit, not new send
grants. Admin stays protected. A business role needs no deployment; an executable
action still needs server implementation and a registered Turkish action definition.
Historical TeamView/AuditView/AccessAdministration/Access.ViewAudit are retained but
are not advertised as independently implemented new actions.

`GET /api/v1/access/users/page` needs ManageUsers; `requests/page` needs
ApproveRequests. SQL applies literal bounded search, status/role filters, stable
ordering, total/counts and page size <=100 before materialization. UI preserves
query/page while opening details. Rejected request is not a new pending request.
Current details and role choices come from persisted identity/role definitions.

`GET roles` supports authorized management/decision callers; `GET roles/actions`
needs AssignRoles. `POST roles/preview` and `PUT roles` require both ManageUsers
and AssignRoles. Impact binds proposed definition, role version, affected users'
access versions and actor/version. Changed assignments invalidate the preview.
Response distinguishes affected users from effective gains/losses through all roles.
Ordinary replacement has no human reason; rejection and disable retain theirs.
Assignment/approval accepts reviewed roleVersions so a changed bundle cannot be
silently assigned. Legacy requests are accepted only for unchanged built-ins at v1.

All administrative mutations share transaction-owned `SecureOps.Access.Administration.v1`;
persisted actor authorization, self-escalation, final Admin removal/disable and
version checks serialize together. Audit failure rolls back the mutation. Bundle
edits advance affected users' AccessVersion; sessions and queued mail revalidate.
An Admin may create a mail role for another approved person, but cannot escalate
their own role assignment or their own existing bundle. No real user is granted
rights by migration or local fixture. Role definition UI retains local edits on
conflict and requires explicit comparison/re-preview. Labels never confer access.

## Access and Session Boundary

The configured authentication handler authenticates the current corporate principal. `ICorporatePrincipalResolver` translates authentication data into a provider-neutral identifier; `IApplicationAccessService` then resolves `Pending`, `Approved`, or `Disabled` status, roles, and capabilities. New users are pending. `GET /api/v1/access/me` exposes that state. Approval, rejection, role replacement, and disable endpoints require explicit access capabilities.

Rejection is terminal for the current request. The user remains non-authorized `Pending`; `/access/me` exposes the latest request as `Rejected`, with no pending request ID, and does not create another request. Reapplication is intentionally unsupported until an explicit business policy defines initiation, cooling-off/reset, and authorization. Administrators retain the complete request history through the access-user read model.

`GET /api/v1/access/users` and `GET /api/v1/access/users/{id}` require `Access.ManageUsers`. They return authoritative status, assigned roles, backend-derived capabilities, latest request/history, and mutation versions. Safe profile enrichment (`DisplayName`, account, email, department, title) uses the existing exact account normalizer and configured identity provider. Missing, invalid, not-found, or unavailable enrichment returns `null`; directory values are never inferred from the principal.

Request decisions require the request `version`; role replacement and disable require the user `version`. Versions change only on access mutations, not on `LastAuthenticatedAt` updates. Stale versions return retryable `AccessConcurrencyConflict`; validation, already-decided requests, invalid user lifecycle, and self-approval use separate stable codes.

The real first Admin may be created only when `BootstrapAdmin:Enabled=true`, a validated OIDC principal has the exact allowed issuer and configured login name, SQL access persistence is active, and no Admin role assignment has ever existed. Login-name matching follows the existing exact account semantics and is ordinal case-insensitive; issuer, subject-derived stable identity, and issuer comparison are ordinal case-sensitive. A serializable SQL transaction locks the canonical Admin role, checks all assignment history including revoked rows, approves the pending request, assigns Admin, and inserts bootstrap/access audit evidence before commit. Leaving the gate enabled cannot create another Admin. Demo/Test compatibility remains a separate synthetic path. Full deployment details are in `docs/24-api-test-deployment-readiness.md` and ADR-0017.

`SessionSecurity` governs a provider-neutral server-side SecureOps application session: 30-minute idle timeout, 12-hour absolute lifetime, and five-minute persisted-activity throttle by default. Its Secure, HttpOnly, SameSite=Lax cookie contains only a protected opaque handle and is never the corporate authentication or authorization source. The UI keeps that handle in a server-side jar keyed by the encrypted browser authentication session and replays it across every typed API client; HTTPS is required except for a same-host loopback HTTP binding, where the handle does not cross a network hop. Rejected handles remain rejected until reauthentication instead of becoming an implicit replacement session. SecureOps logout ends the API application session first, then the local UI cookie, and invokes provider sign-out only when explicitly configured. Current access status and `AccessVersion` are revalidated, so disable/revocation invalidates effective sessions.

OIDC handlers are present but `Oidc:Enabled=false` by default. Enabling requires a validated issuer authority, explicit HTTPS metadata address, client ID, API audience, explicit `None` or `ClientSecretPost` client authentication, local callback paths, `openid` scope, and an explicit PKCE mode. The interactive UI validates and normalizes the ID-token identity; the API independently validates the relayed RS256 access token. Both use bounded reviewed claim names and `issuer + sub` as the opaque persisted identity. `uygulama-role` is diagnostic evidence only.

## Capability Matrix

### Current Matrix And Expanded Continuation, 2026-09-15

Fixed start: `4a1700a6c0e346edf2c47a24fe6402538678db1d`, clean combined worktree.
Sources: AccessRoleCatalog, API policy registration/controllers and execution
services. SQL stores role IDs/assignments; capability bundles remain compiled,
not database-managed. Corporate permission inventory was not accessible.
This table supersedes the abbreviated historical paragraph following it.

| Rol | Amacı ve mevcut yetkileri |
|---|---|
| Admin / Sistem Yöneticisi | Aşağıdaki 26 yetkinin tamamı; erişim yönetimi ve OCO hazırlama dahil. SMTP gönderimi henüz yok. |
| Lead / Takım Lideri | Bağlantılar; kimlik/temel grup sorguları; OR görüntüleme/önizleme/Jira oluşturma/yeniden deneme/tanılama. TeamView ve SystemDiagnostics de atanır. Erişim yöneticisi veya In Use koordinatörü değildir. |
| Operator / Operasyon Uzmanı | Bağlantılar, OR görüntüleme, salt okunur Jira önizleme/inceleme beyanı; TeamView. Jira oluşturamaz; OCO/In Use yetkisi vermez. |
| JiraPublisher / Jira İşlem Yetkilisi | Bağlantılar ve OR görüntüleme/önizleme/oluşturma/yeniden deneme; Lead'in kimlik/grup/tanılama hakları yok. |
| Auditor / Denetim Görüntüleyicisi | Bağlantılar, OR görüntüleme/tanılama, yönetim raporu; AuditView ve Access.ViewAudit kodları. Kullanıcı değiştirmez. |
| ReadOnly / Sadece Görüntüleme | OR ve bağlantıları görüntüler; kendi favori/gruplarını düzenler. Tüm uygulamada mutlak yazma yasağı değildir. |
| ResourceCurator / Bağlantı Yöneticisi | Bağlantıları kullanır, paylaşılan bağlantı/kategorileri yönetir; hedef sistemlerde yetki vermez. |
| InUseReviewer / In Use İnceleyicisi | In Use görüntüleme, yerel inceleme/rapor; atama zorunlu değil, başka inceleyicinin atandığı kayıt da incelenebilir. |
| InUseCoordinator / In Use Koordinatörü | İnceleyici haklarına ek açık kaynak yenileme ve isteğe bağlı inceleyici atama. |

Yetkiler rol kümelerinin birleşimidir; ordinal rol sıralaması yoktur. Profil
Mail/unvan/bölüm yetki kazandırmaz. Mevcut atamalar değiştirilmedi. Rol kaldırmak,
başka bir rolün sağladığı aynı yetkiyi kaldırmaz. Yeni hassas işlemler varsayılan
kapalı kalacak; dinamik sunucu işlem kataloğu henüz uygulanmadı.

| Yetki | Gerçek rota / işlem (`/api/v1` altında) |
|---|---|
| Announcements.Drafts | announcements: kendi taslak/preview/eml/preparations/source profiles/jobs/proposal/apply; göndermez. |
| InUse.View | in-use liste/detay/rapor okuma; diğer In Use işlemlerinin ortak koşulu. |
| InUse.Review | in-use draft/report ve yalnızca engelli completion-intent günlüğü. |
| InUse.Assign | in-use assignees/assignment. |
| InUse.Refresh | in-use refresh; relationship-evidence ayrıca OR tanılama ister. |
| Resources.View | resources liste/resolve/me/favori/kişisel grup. |
| Resources.Manage | resources categories/links ortak katalog mutasyonları; View da gerekir. |
| Identity.Lookup | identity lookup/bulk-lookup/lookup-capabilities; Bulk aynı yetkinin alias'ı. |
| Identity.Groups.View | directory principals groups/memberships/membership-paths/account-health/service-evidence ve groups/lookup. |
| Identity.Groups.Members.View | directory groups/members ve groups/analysis. |
| Identity.PrivilegedGroups.View | directory principals/privileged-memberships. |
| Identity.Groups.Export | directory groups/export; ilgili üyelik erişimi ayrıca denetlenir. |
| TeamView | Atanmış kod; ayrı ekip CRUD/sorgu endpoint'i bulunmadı. |
| AuditView | Atanmış kod; genel audit sorgu ekranı gelecekteki aşama, çalışan sorgu API'si değil. |
| AccessAdministration | Uyumluluk kodu; ayrı kullanıcı/talep yetkilerinin yerine geçmez. |
| SystemDiagnostics | identity/lookup/cache-diagnostics ve güvenli sağlık/tanılama; hedef sunucuda komut çalıştırmaz. |
| OperationalRecords.View | OR import/stored browse/detay; salt okunur kaynak. |
| OperationalRecords.CreateJiraPreview | OR jira-preview/jira-review; onay/pozitif politika değildir. |
| OperationalRecords.CreateJira | OR jira; exact-record politika, sürüm ve dış yazma kapıları ayrıca gerekir. |
| OperationalRecords.Retry | OR retry; belirsiz sonuç otomatik tekrar anlamına gelmez. |
| OperationalRecords.ViewDiagnostics | OR tanılama alanları ve ayrıca yetkili In Use ilişki kanıtı. |
| Access.ManageUsers | access/users liste/detay/disable ve sessions active/revoke. |
| Access.ApproveRequests | access/requests liste/approve/reject; kullanıcı yönetiminden bağımsız. |
| Access.AssignRoles | access/users/{id}/roles; kullanıcı sürümüyle tam rol kümesi değişimi. |
| Access.ViewAudit | Atanmış kod; ayrı erişim audit sorgu endpoint'i bulunmadı. |
| Reporting.ManagementView | reporting/management summary/operators; in-use overview ayrıca InUse.View ister. |

Ordinary role replacement omits Reason in DTO/client/service/repository. Legacy
extra JSON reason is ignored, not represented as new human justification. SQL
commits AccessRolesChanged (old/new roles/capabilities, actor, UTC time, versions,
Applied outcome, SystemGenerated description) with the mutation. Per-role audit
and session termination remain. Approval/rejection/disable and historical reasons
are preserved. SQL audit failure rolls back roles/version.

Remaining: versioned role bundles and impact preview; grant authority/self-edit/
last-admin guards; definition revocation propagation; bounded SQL search/filter/
count/paging; late-response/unsaved-edit recovery across the three redesigned
screens. Current lists still fetch complete inventories, not server-side pages.

Roles are application records, not direct AD-group grants. `Admin` has all implemented capabilities. `Lead` has identity lookup, Operational Record view/create/retry/diagnostics, team view, and diagnostics. `Operator` can view records and create previews. `JiraPublisher` can view/preview/create/retry. `Auditor` can view audit and Operational Record diagnostics. `ReadOnly` can only view Operational Records.

## Reliability Controls

Jira create/retry accepts optional `Idempotency-Key`; absent keys use a deterministic actor, command, and target digest. SQL `ops.CommandExecutions` preserves completed/failed/in-progress state across restarts. A separate bounded Operational Record claim records actor and expiry. Expired claims can be recovered. An expired source-close stage can resume because the Jira key is already durable. Interrupted `CreatingJira` remains fail-closed for reconciliation because remote outcome is unknown; automatic recovery requires a future Jira idempotency contract.

Immediately before Jira create and source close, `IOperationalRecordClient.GetByIdAsync` must return an existing open record whose explicit version token, or deterministic bounded-state hash, matches the imported token. Without native source ETag/conditional update support, a small check-to-write race remains and must be resolved by the future adapter contract.

Source refresh and classification are not allowed to overwrite a workflow state that has advanced beyond initial classification. Unknown Jira outcomes remain `JiraCreateFailed` with `ReconciliationRequired=true`; command replay, a new create key, and retry cannot invoke Jira again. Test-host-only barriers verify that a second actor cannot overwrite an active claim or its version while the first operation is in progress.

Identity exact reads use an optional bounded in-process TTL cache and single-flight provider call keyed only by normalized exact account. Exceptions are not cached. Aggregate hit/miss/provider/coalesced counters contain no account labels. Authorization is evaluated before cache access.

Named fixed-window rate policies partition by authenticated actor plus operation: identity lookup, bulk lookup, Operational Record refresh, Jira preview, Jira create, and retry. Rejection is safe RFC ProblemDetails with correlation data. Rate limiting does not replace idempotency.

## Swagger TEST Release Gate

TEST requires `ASPNETCORE_ENVIRONMENT=Test` or `Demo`, `Swagger__Enabled=true`, explicit Demo authentication settings when that compatibility path is used, and the three Swashbuckle runtime assemblies represented in `SecureOps.Api.deps.json`. Run `scripts/powershell/Test-ApiTestSwaggerReadiness.ps1` against the publish directory with explicit environment and Swagger inputs. The gate validates files, dependency manifest, and `net8.0`; it does not modify server configuration. Production Swagger UI remains disabled and JSON remains Admin-capability protected when enabled.

`scripts/release/Test-ApiReleasePayload.ps1` additionally rejects source/PDB/test/log payloads, scans text configuration for credential-like assignments, and scans all files for caller-supplied personal-path markers before packaging.

## Persistence

The application never runs SQL migrations. DBA review/execution of migrations 001-008 is required before selecting SQL access, application-session, or Operational Record persistence. Migration 007 adds `security.ApplicationSessions` and a limited reporting view; migration 008 adds nullable OIDC profile metadata to `security.Users`. Runtime needs only the documented object-level `SELECT`, `INSERT`, and `UPDATE` grants; no DDL or DELETE permission is required.
