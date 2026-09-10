/* Preserve v1 negative evidence; admit only the separately versioned positive policy shape. */
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'ops.InUseRecords', N'U') IS NULL
    OR NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE parent_object_id = OBJECT_ID(N'ops.OperationalRecords') AND name = N'CK_OperationalRecords_SdmEvaluation')
    THROW 51000, 'SDM pilot prerequisites 001-012 are missing.', 1;
ALTER TABLE ops.OperationalRecords DROP CONSTRAINT CK_OperationalRecords_SdmEvaluation;
ALTER TABLE ops.OperationalRecords WITH CHECK ADD CONSTRAINT CK_OperationalRecords_SdmEvaluation CHECK
(SdmEvaluationJson IS NULL OR (ISJSON(SdmEvaluationJson) = 1
    AND ISNULL(LEN(JSON_VALUE(SdmEvaluationJson, '$.SourceFingerprint')), 0) = 64
    AND ISNULL(LEN(JSON_VALUE(SdmEvaluationJson, '$.Result.InputHash')), 0) = 64
    AND JSON_VALUE(SdmEvaluationJson, '$.SourceFingerprint') COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%'
    AND JSON_VALUE(SdmEvaluationJson, '$.Result.InputHash') COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%'
    AND ISNULL(LEN(JSON_VALUE(SdmEvaluationJson, '$.Result.RuleSetVersion')), 0) BETWEEN 1 AND 64
    AND JSON_QUERY(SdmEvaluationJson, '$.Result.ReasonCodes') IS NOT NULL
    AND JSON_QUERY(SdmEvaluationJson, '$.Result.BlockingConditions') IS NOT NULL
    AND ISNULL(JSON_VALUE(SdmEvaluationJson, '$.Result.EvaluationStale'), '') IN ('true', 'false')
    AND ISNULL(JSON_VALUE(SdmEvaluationJson, '$.Result.SourceChanged'), '') IN ('true', 'false')
    AND TRY_CONVERT(datetimeoffset(7), JSON_VALUE(SdmEvaluationJson, '$.EvaluatedAt')) IS NOT NULL
    AND ((ISNULL(JSON_VALUE(SdmEvaluationJson, '$.Result.RecommendedClassification'), '') IN ('5', '6')
        AND ISNULL(JSON_VALUE(SdmEvaluationJson, '$.Result.SdmCandidateRecommended'), '') = 'false'
        AND ISNULL(JSON_VALUE(SdmEvaluationJson, '$.Result.JiraEligible'), '') = 'false'
        AND ISNULL(JSON_VALUE(SdmEvaluationJson, '$.Result.ExternalWriteEligible'), '') = 'false')
      OR (ISNULL(JSON_VALUE(SdmEvaluationJson, '$.Result.RuleSetVersion'), '') COLLATE Latin1_General_100_BIN2 = 'WASAS-SDM-PILOT-2026.09-v1'
        AND ISNULL(JSON_VALUE(SdmEvaluationJson, '$.Result.RecommendedClassification'), '') = '0'
        AND ISNULL(JSON_VALUE(SdmEvaluationJson, '$.Result.SdmCandidateRecommended'), '') = 'true'
        AND ISNULL(JSON_VALUE(SdmEvaluationJson, '$.Result.JiraEligible'), '') = 'true'
        AND ISNULL(JSON_VALUE(SdmEvaluationJson, '$.Result.SourceChanged'), '') = 'false'
        AND ISNULL(JSON_VALUE(SdmEvaluationJson, '$.Result.EvaluationStale'), '') = 'false'
        AND ((JSON_QUERY(SdmEvaluationJson, '$.Result.BlockingConditions') = '[]'
            AND ISNULL(JSON_VALUE(SdmEvaluationJson, '$.Result.ExternalWriteEligible'), '') = 'true')
          OR (JSON_QUERY(SdmEvaluationJson, '$.Result.BlockingConditions') = '["ExternalWritesDisabled"]'
            AND ISNULL(JSON_VALUE(SdmEvaluationJson, '$.Result.ExternalWriteEligible'), '') = 'false'))))));
COMMIT TRANSACTION;
GO
