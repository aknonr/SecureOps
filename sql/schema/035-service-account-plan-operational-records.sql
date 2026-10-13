-- Number reserved by the module owner in koordinasyon-ortak\00-ORTAK-MERGE-SIRASI.md, 2026-10-12 (OR-2, ADR-0030 PROPOSED).
-- Preserve the reviewed SA-008 DDL; resolve SQLCMD paths from sql/migrations.
IF OBJECT_ID(N'svcacct.ChangePlans', N'U') IS NULL OR OBJECT_ID(N'svcacct.ChangePlanPreviews', N'U') IS NULL
    OR NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'svcacct.Evidence') AND name = N'CK_SaEvidence_Owner'
                   AND definition LIKE N'%''ChangePlan''%')
    THROW 51400, 'Service account plan OR submissions 035 requires reviewed 033 and 034.', 1;
GO
:r ../pending/service-accounts/SA-008-plan-operational-records.sql
