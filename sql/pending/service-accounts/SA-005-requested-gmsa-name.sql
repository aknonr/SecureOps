/*
  Service Accounts module — candidate 5 (SA-005), numbered 031 by the module owner (2026-10-05).
  Requires 025 (svcacct.WorkRequests, svcacct.IdentityTransitions).

  The gMSA name a conversion will use, recorded where the work is requested and where the transition is tracked, so a
  name that Active Directory would shorten (more than 15 characters without a domain prefix, UPN suffix or trailing $)
  is caught before the conversion. Additive only:
  - WorkRequests.RequestedGmsaName nvarchar(256) NULL;
  - IdentityTransitions.RequestedGmsaName nvarchar(256) NULL.
  Both columns are nullable without a default: existing rows are not rewritten and stay NULL ("no name recorded").
  The API validates the name (gMSA work types only, at most 15 characters as counted above); the column only bounds it.
  No new grants: svcacct_api_runtime already holds table-level SELECT, INSERT, UPDATE on both tables (SA-API-permissions.sql)
  and the Worker role needs nothing new. No role, grant, row or other column is changed.
  Refuses replay. Run with SQLCMD -I -b. No down script: rollback keeps the columns and their values.
*/
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
IF OBJECT_ID(N'svcacct.WorkRequests', N'U') IS NULL OR OBJECT_ID(N'svcacct.IdentityTransitions', N'U') IS NULL
    THROW 51370, 'Service Accounts candidate 5 requires 025 (svcacct.WorkRequests, svcacct.IdentityTransitions).', 1;
IF COL_LENGTH(N'svcacct.WorkRequests', N'RequestedGmsaName') IS NOT NULL OR COL_LENGTH(N'svcacct.IdentityTransitions', N'RequestedGmsaName') IS NOT NULL
    THROW 51370, 'Service Accounts candidate 5 already applied; compare definitions, do not replay.', 1;
BEGIN TRANSACTION;

ALTER TABLE svcacct.WorkRequests ADD RequestedGmsaName nvarchar(256) NULL;
ALTER TABLE svcacct.IdentityTransitions ADD RequestedGmsaName nvarchar(256) NULL;
GO
COMMIT TRANSACTION;
GO
