/* Additive offline contract. DBA execution only, after migrations 001-008. */
IF OBJECT_ID(N'ops.OperationalRecords', N'U') IS NULL
    OR OBJECT_ID(N'ops.OperationalRecordWorkflowHistory', N'U') IS NULL
    OR OBJECT_ID(N'audit.AuditLog', N'U') IS NULL
    THROW 51000, 'SDM evaluation prerequisites are missing.', 1;
GO

/* NULL is unevaluated for existing rows. History triggers remain enabled. */
IF COL_LENGTH(N'ops.OperationalRecords', N'SdmEvaluationJson') IS NULL
    ALTER TABLE ops.OperationalRecords ADD SdmEvaluationJson nvarchar(4000) NULL;
IF COL_LENGTH(N'ops.OperationalRecordWorkflowHistory', N'SdmEvaluationJson') IS NULL
    ALTER TABLE ops.OperationalRecordWorkflowHistory ADD SdmEvaluationJson nvarchar(4000) NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_OperationalRecords_SdmEvaluation')
    ALTER TABLE ops.OperationalRecords WITH CHECK ADD CONSTRAINT CK_OperationalRecords_SdmEvaluation CHECK
    (SdmEvaluationJson IS NULL OR (ISJSON(SdmEvaluationJson) = 1
        AND ISNULL(LEN(JSON_VALUE(SdmEvaluationJson, '$.SourceFingerprint')), 0) = 64
        AND ISNULL(LEN(JSON_VALUE(SdmEvaluationJson, '$.Result.InputHash')), 0) = 64
        AND JSON_VALUE(SdmEvaluationJson, '$.SourceFingerprint') COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%'
        AND JSON_VALUE(SdmEvaluationJson, '$.Result.InputHash') COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%'
        AND ISNULL(LEN(JSON_VALUE(SdmEvaluationJson, '$.Result.RuleSetVersion')), 0) BETWEEN 1 AND 64
        AND ISNULL(JSON_VALUE(SdmEvaluationJson, '$.Result.RecommendedClassification'), '') IN ('5', '6')
        AND JSON_QUERY(SdmEvaluationJson, '$.Result.ReasonCodes') IS NOT NULL
        AND JSON_QUERY(SdmEvaluationJson, '$.Result.BlockingConditions') IS NOT NULL
        AND ISNULL(JSON_VALUE(SdmEvaluationJson, '$.Result.SdmCandidateRecommended'), '') = 'false'
        AND ISNULL(JSON_VALUE(SdmEvaluationJson, '$.Result.JiraEligible'), '') = 'false'
        AND ISNULL(JSON_VALUE(SdmEvaluationJson, '$.Result.ExternalWriteEligible'), '') = 'false'
        AND ISNULL(JSON_VALUE(SdmEvaluationJson, '$.Result.EvaluationStale'), '') IN ('true', 'false')
        AND ISNULL(JSON_VALUE(SdmEvaluationJson, '$.Result.SourceChanged'), '') IN ('true', 'false')
        AND TRY_CONVERT(datetimeoffset(7), JSON_VALUE(SdmEvaluationJson, '$.EvaluatedAt')) IS NOT NULL));
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_WorkflowHistory_SdmEvaluation')
    ALTER TABLE ops.OperationalRecordWorkflowHistory WITH CHECK ADD CONSTRAINT CK_WorkflowHistory_SdmEvaluation CHECK
    (SdmEvaluationJson IS NULL OR ISJSON(SdmEvaluationJson) = 1);
GO
