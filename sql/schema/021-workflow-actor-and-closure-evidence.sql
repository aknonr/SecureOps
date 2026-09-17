SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
IF OBJECT_ID(N'ops.OperationEvents') IS NULL THROW 51210,'Migration 020 required.',1;
IF COL_LENGTH(N'ops.JiraTransfers',N'InitiatorJson') IS NOT NULL THROW 51210,'021 columns exist; compare definitions, do not replay.',1;
BEGIN TRANSACTION;
ALTER TABLE ops.JiraTransfers ADD InitiatorJson nvarchar(4000) NULL, ClosureEvidenceJson nvarchar(4000) NULL, InputVersion bigint NULL;
EXEC(N'ALTER TABLE ops.JiraTransfers ADD CONSTRAINT CK_JiraTransfers_ActorEvidence CHECK(InitiatorJson IS NULL OR ISJSON(InitiatorJson)=1),
    CONSTRAINT CK_JiraTransfers_ClosureEvidence CHECK(ClosureEvidenceJson IS NULL OR (ISJSON(ClosureEvidenceJson)=1 AND JSON_VALUE(ClosureEvidenceJson,''$.State'')=''VerifiedClosed''));');
-- Existing rows stay NULL: neither historical actors nor source closure are reconstructed.
COMMIT TRANSACTION;
