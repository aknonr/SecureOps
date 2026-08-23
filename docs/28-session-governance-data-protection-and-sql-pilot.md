# Session Governance, Data Protection, and SQL Pilot Readiness

## Runtime Contract

`SessionSecurity` controls `IdleTimeoutMinutes` (default 30), `AbsoluteLifetimeHours` (default 12), `ActivityPersistenceIntervalMinutes` (default 5), `RepositoryProvider`, `CookieName`, and bounded administrative page size. The application-session cookie is an opaque protected handle only. Server state, current access status, and `AccessVersion` are authoritative.

`DataProtection` controls `Mode`, `ApplicationName`, `KeyRingPath`, and optional `CertificateThumbprint`. Supported modes are `Ephemeral` for local Development/Demo/Test only, `FileSystemDpapi` for a single Windows node, and `FileSystemCertificate` for a shared future multi-node key ring. Pilot and Production reject ephemeral or incomplete configuration at startup. No key or certificate material belongs in Git.

## API Contract

- `GET /api/v1/sessions/current` returns the current session metadata without the session secret.
- `GET /api/v1/sessions/active?page=1&pageSize=50` requires `Access.Users.Manage` and returns bounded, non-network session metadata.
- `POST /api/v1/sessions/revoke` requires `Access.Users.Manage` and revokes an exact session identifier with a bounded operational reason.
- `POST /api/v1/access/logout` ends the current SecureOps session and clears its handle. Negotiate remains browser/host managed and may authenticate a later request again.

## Data Protection Deployment

For one pilot node, create a server-owned key-ring directory outside the deployment payload, grant the App Pool identity read/write/create access only to that directory, and select local-machine DPAPI protection. Back up the key ring under the same access and retention controls as other authentication material. For multiple nodes, use one access-controlled shared key ring and certificate protection where each node can read the private key; keep the same application name on all nodes.

## Windows Authentication Boundary

Negotiate must reach IIS/API through a topology that preserves Windows authentication. An HTTPS-offloading proxy can forward application traffic, but the approved SPN, kernel-mode/application-pool identity, delegation, and proxy authentication behavior must be validated by IIS/AD owners. The SecureOps session cookie does not replace, tunnel, or recover corporate authentication.

## SQL Ownership

Migration `007-application-session-governance.sql` is reviewed and executed by the DBA/deployment identity after migrations 001-006. The runtime identity receives only documented object-level `SELECT`, `INSERT`, and `UPDATE` grants. It receives no `DELETE`, `ALTER`, `CREATE`, `CONTROL`, `db_owner`, or migration permission. The application never executes the migration.
