# Session Governance, Data Protection, and SQL Pilot Readiness

## Runtime Contract

`SessionSecurity` controls `IdleTimeoutMinutes` (default 30), `AbsoluteLifetimeHours` (default 12), `ActivityPersistenceIntervalMinutes` (default 5), `RepositoryProvider`, `CookieName`, and bounded administrative page size. The application-session cookie is an opaque protected handle only. Server state, current access status, and `AccessVersion` are authoritative.

`DataProtection` controls `Mode`, `ApplicationName`, `KeyRingPath`, and optional `CertificateThumbprint` independently in the API and UI processes. Supported modes are `Ephemeral` for local Development/Demo/Test only, `FileSystemDpapi` for a single Windows node, and `FileSystemCertificate` for a shared future multi-node key ring. Pilot and Production reject ephemeral or incomplete configuration at startup. No key or certificate material belongs in Git.

## API Contract

- `GET /api/v1/sessions/current` returns the current session metadata without the session secret.
- `GET /api/v1/sessions/active?page=1&pageSize=50` requires `Access.Users.Manage` and returns bounded, non-network session metadata.
- `POST /api/v1/sessions/revoke` requires `Access.Users.Manage` and revokes an exact session identifier with a bounded operational reason.
- `POST /api/v1/access/logout` ends the current SecureOps session and clears its handle. Negotiate remains browser/host managed and may authenticate a later request again.

Session responses retain internal IDs for backend correctness and add nullable persisted identity metadata: principal/normalized principal, authentication provider, display name when a persisted value exists, and `isCurrent`. The current access model does not persist a display name, so the API returns null rather than contacting AD or inventing one.

Administrative listing now atomically marks previously unvisited idle/absolute expirations terminal before returning active rows. The server-rendered UI still must correlate all typed API clients to one browser authentication session and call API logout; merging missing-cookie requests by user is prohibited because separate/private browsers must remain distinct.

## Data Protection Deployment

For one pilot node, create separate server-owned API and UI key-ring directories outside both deployment payloads. Grant each App Pool identity read/write/create access only to its own directory and select local-machine DPAPI protection. Use stable discriminators `SecureOps.Api` and `SecureOps.Ui`; changing either invalidates that application's protected payloads. Back up both key rings under the same access and retention controls as other authentication material.

The UI ring protects its authentication ticket and antiforgery cookie. The API ring protects the application-session handle and directory continuation tokens. API persistence alone cannot prevent UI `Unprotect ticket failed` or antiforgery key-not-found errors. After replacing an ephemeral ring, pre-existing cookies are intentionally unreadable and must be cleared once.

For multiple nodes, local-machine DPAPI cannot protect a shared ring. Use separate access-controlled shared rings with `FileSystemCertificate`, deploy the approved certificate/private key to every participating node, grant each App Pool only the required private-key access, and keep each application's discriminator stable across its nodes.

## Windows Authentication Boundary

Negotiate must reach IIS/API through a topology that preserves Windows authentication. An HTTPS-offloading proxy can forward application traffic, but the approved SPN, kernel-mode/application-pool identity, delegation, and proxy authentication behavior must be validated by IIS/AD owners. The SecureOps session cookie does not replace, tunnel, or recover corporate authentication.

## SQL Ownership

Migration `007-application-session-governance.sql` is reviewed and executed by the DBA/deployment identity after migrations 001-006. The runtime identity receives only documented object-level `SELECT`, `INSERT`, and `UPDATE` grants. It receives no `DELETE`, `ALTER`, `CREATE`, `CONTROL`, `db_owner`, or migration permission. The application never executes the migration.
