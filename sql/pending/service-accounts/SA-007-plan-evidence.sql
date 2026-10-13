/*
  Service Accounts module — candidate 7 (SA-007), numbered 034 by the module owner (2026-10-12).
  Requires 025 (svcacct.Evidence) and 033 (svcacct.ChangePlans).

  Plan evidence (docs/service-accounts/CHANGE-PLAN-DESIGN.md, PR 2): evidence files of a gMSA conversion change plan are
  kept in the existing svcacct.Evidence table with OwnerEntityType 'ChangePlan' and OwnerEntityId = the plan. The only
  change is the owner-type check: CK_SaEvidence_Owner is dropped and created again under the same name with 'ChangePlan'
  added, WITH CHECK, so every existing row is validated and the constraint stays trusted. No table, column, row, trigger,
  role or permission changes; the table stays immutable (TR_SaEvidence_Immutable) and the API role keeps its existing
  Evidence permissions. The current check must be exactly the eight owner types of 025, otherwise the script stops before
  any change. Refuses replay and a missing prerequisite (51396). One transaction. Run with SQLCMD -I -b. No down script:
  older binaries never write 'ChangePlan'; going back is described in docs/service-accounts/DBA-034-TR.md.
*/
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
IF OBJECT_ID(N'svcacct.Evidence', N'U') IS NULL OR OBJECT_ID(N'svcacct.ChangePlans', N'U') IS NULL
    THROW 51396, 'Service Accounts candidate 7 requires 025 and 033.', 1;
DECLARE @definition nvarchar(max) = (SELECT definition FROM sys.check_constraints
    WHERE parent_object_id = OBJECT_ID(N'svcacct.Evidence') AND name = N'CK_SaEvidence_Owner');
IF @definition LIKE N'%''ChangePlan''%'
    THROW 51396, 'Service Accounts candidate 7 already applied; compare definitions, do not replay.', 1;
IF @definition IS NULL
    OR (LEN(@definition) - LEN(REPLACE(@definition, N'[OwnerEntityType]=', N''))) / LEN(N'[OwnerEntityType]=') <> 8
    OR @definition NOT LIKE N'%''Account''%' OR @definition NOT LIKE N'%''Request''%' OR @definition NOT LIKE N'%''Action''%'
    OR @definition NOT LIKE N'%''Communication''%' OR @definition NOT LIKE N'%''Finding''%' OR @definition NOT LIKE N'%''Handover''%'
    OR @definition NOT LIKE N'%''Transition''%' OR @definition NOT LIKE N'%''Ownership''%'
    THROW 51396, 'Service Accounts candidate 7 expects the CK_SaEvidence_Owner check of 025; compare definitions.', 1;
BEGIN TRANSACTION;
ALTER TABLE svcacct.Evidence DROP CONSTRAINT CK_SaEvidence_Owner;
ALTER TABLE svcacct.Evidence WITH CHECK ADD CONSTRAINT CK_SaEvidence_Owner CHECK (OwnerEntityType IN ('Account','Request','Action','Communication','Finding','Handover','Transition','Ownership','ChangePlan'));
COMMIT TRANSACTION;
