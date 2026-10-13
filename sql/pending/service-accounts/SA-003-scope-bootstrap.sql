/*
  Service Accounts module — UNNUMBERED CANDIDATE 3 (not a deployable migration).
  Requires 025 (svcacct.ScopeGrants). Codex reserves the migration number before promotion.

  One-time first scope grant (ADR-0026). Until now CK_SaScopeGrants_NoSelfGrant made the very first grant impossible
  when only one module administrator exists. This candidate:
  - adds IsBootstrap bit NOT NULL DEFAULT 0 (existing rows become 0);
  - replaces the self-grant check with: UserId <> GrantedBy, unless the row is the bootstrap row, which must be an
    "All" grant to its own grantor;
  - adds a filtered unique index that allows at most ONE bootstrap row in the table, ever (revoked rows are kept and
    never deleted, so a revoked bootstrap row still blocks a second one).
  The service additionally refuses the bootstrap when any grant row exists (active or revoked).
  Refuses replay. Run with SQLCMD -I -b. No new grants needed: the API role already inserts into svcacct.ScopeGrants.
  Rollback: re-adding the original CHECK succeeds only while no bootstrap row exists; keep the column otherwise.
*/
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
IF OBJECT_ID(N'svcacct.ScopeGrants', N'U') IS NULL
    THROW 51350, 'Service Accounts candidate 3 requires 025 (svcacct.ScopeGrants).', 1;
IF COL_LENGTH(N'svcacct.ScopeGrants', N'IsBootstrap') IS NOT NULL
    THROW 51350, 'Service Accounts candidate 3 already applied; compare definitions, do not replay.', 1;
IF OBJECT_ID(N'svcacct.CK_SaScopeGrants_NoSelfGrant', N'C') IS NULL
    THROW 51350, 'Expected CK_SaScopeGrants_NoSelfGrant from 025; compare definitions before applying.', 1;
BEGIN TRANSACTION;

ALTER TABLE svcacct.ScopeGrants ADD IsBootstrap bit NOT NULL CONSTRAINT DF_SaScopeGrants_IsBootstrap DEFAULT (0);
GO
ALTER TABLE svcacct.ScopeGrants DROP CONSTRAINT CK_SaScopeGrants_NoSelfGrant;
ALTER TABLE svcacct.ScopeGrants WITH CHECK ADD CONSTRAINT CK_SaScopeGrants_NoSelfGrant CHECK (
    (IsBootstrap = 0 AND UserId <> GrantedBy)
    OR (IsBootstrap = 1 AND UserId = GrantedBy AND ScopeKind = 'All' AND OrganizationId IS NULL AND TeamId IS NULL));
CREATE UNIQUE INDEX UX_SaScopeGrants_OneBootstrap ON svcacct.ScopeGrants(IsBootstrap) WHERE IsBootstrap = 1;
GO
COMMIT TRANSACTION;
GO
