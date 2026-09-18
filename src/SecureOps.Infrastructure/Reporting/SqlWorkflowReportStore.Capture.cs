namespace SecureOps.Infrastructure.Reporting;

public sealed partial class SqlWorkflowReportStore
{
    // All projections are scoped before materialization. No source/audit authorization event is promoted to remote success.
    private const string _captureSql = """
        SELECT TOP(0) Metric,LogicalId,Module,RecordId,Reference,RecordType,Status,Actor,Assignee,OccurredAt,Detail
        INTO #facts FROM reporting.WorkflowFacts;

        IF @inUse=1
        BEGIN
            SELECT r.*,JSON_VALUE(r.RecordJson,'$.Draft.ReviewedByLabel') AS Reviewer,
                TRY_CONVERT(datetimeoffset,JSON_VALUE(r.RecordJson,'$.Draft.ReviewedAt')) AS ReviewedAt,
                JSON_VALUE(r.RecordJson,'$.AssigneeLabel') AS Assignee,
                (SELECT COUNT(*) FROM OPENJSON(r.RecordJson,'$.Source.Servers')) AS Servers,
                (SELECT COUNT(*) FROM OPENJSON(r.RecordJson,'$.Source.Servers') s WHERE
                    (SELECT COUNT(DISTINCT JSON_VALUE(a.value,'$.Check')) FROM OPENJSON(r.RecordJson,'$.Draft.Answers') a
                     WHERE JSON_VALUE(a.value,'$.ServerId')=JSON_VALUE(s.value,'$.Id')
                       AND JSON_VALUE(a.value,'$.Check') IN ('InternetOut','InternetIn','Microsegmented')
                       AND JSON_VALUE(a.value,'$.Value') IN ('Yes','No'))=3
                    AND JSON_VALUE(r.RecordJson,'$.Draft.SourceVersion')=JSON_VALUE(r.RecordJson,'$.SourceVersion')) AS Answered
            INTO #inuse FROM ops.InUseRecords r
            WHERE @IncludeSynthetic=1 OR JSON_VALUE(r.RecordJson,'$.Source.Synthetic')='false';
            INSERT #facts
            SELECT metric.KeyName,CONVERT(nvarchar(200),r.Id),'InUse',r.Id,r.Code,'InUse',r.ReviewStatus,r.Reviewer,r.Assignee,r.ReviewedAt,
                CASE WHEN r.Servers=0 THEN N'Kaynak sunucu listesi eksik' WHEN r.Answered<r.Servers THEN N'Eksik cevapları tamamlayın' ELSE N'Rapor ve kaynak sonuçlarını ayrı kontrol edin' END
            FROM #inuse r CROSS APPLY(VALUES
                ('InUse.Backlog',CASE WHEN (r.Answered<r.Servers OR r.Servers=0) AND COALESCE(JSON_VALUE(r.RecordJson,'$.Source.Lifecycle.Value'),'Unknown')<>'Closed' THEN 1 ELSE 0 END),
                ('InUse.Open',CASE WHEN JSON_VALUE(r.RecordJson,'$.Source.Lifecycle.Value')='Open' THEN 1 ELSE 0 END),
                ('InUse.Reviewed',CASE WHEN r.Answered=r.Servers AND r.Servers>0 THEN 1 ELSE 0 END),
                ('InUse.Assigned',CASE WHEN r.AssigneeId IS NOT NULL THEN 1 ELSE 0 END),
                ('InUse.Unassigned',CASE WHEN r.AssigneeId IS NULL THEN 1 ELSE 0 END)) metric(KeyName,IncludeRow)
            WHERE metric.IncludeRow=1;
            INSERT #facts
            SELECT 'InUse.Servers',CONVERT(nvarchar(36),r.Id)+':'+JSON_VALUE(s.value,'$.Id'),'InUse',r.Id,r.Code,'InUse','Reviewed',r.Reviewer,r.Assignee,r.ReviewedAt,
                N'Güncel kaynak sürümünde üç açık cevap; izleme yapılandırması kanıtı değildir'
            FROM #inuse r CROSS APPLY OPENJSON(r.RecordJson,'$.Source.Servers') s
            WHERE JSON_VALUE(r.RecordJson,'$.Draft.SourceVersion')=JSON_VALUE(r.RecordJson,'$.SourceVersion')
                AND (SELECT COUNT(DISTINCT JSON_VALUE(a.value,'$.Check')) FROM OPENJSON(r.RecordJson,'$.Draft.Answers') a
                    WHERE JSON_VALUE(a.value,'$.ServerId')=JSON_VALUE(s.value,'$.Id')
                    AND JSON_VALUE(a.value,'$.Check') IN ('InternetOut','InternetIn','Microsegmented')
                    AND JSON_VALUE(a.value,'$.Value') IN ('Yes','No'))=3;
            INSERT #facts
            SELECT 'InUse.Archived',CONVERT(nvarchar(36),a.RecordId)+':'+CONVERT(nvarchar(20),a.ReportVersion),'InUse',a.RecordId,r.Code,'InUse','Archived',a.PreparedByLabel,r.Assignee,a.PreparedAt,
                N'SHA-256: '+a.Sha256
            FROM reporting.InUseArchiveReceipts a JOIN #inuse r ON r.Id=a.RecordId
            WHERE a.PreparedAt>=@From AND a.PreparedAt<@To AND (@IncludeSynthetic=1 OR a.Synthetic=0);
            INSERT #facts
            SELECT 'InUse.'+e.State,CONVERT(nvarchar(200),e.OperationId),'InUse',r.Id,r.Code,'InUse',e.State,
                JSON_VALUE(e.IntentJson,'$.InitiatorLabel'),r.Assignee,e.UpdatedAt,N'Kayıtlı adımları inceleyin; belirsiz etkiyi tekrar yürütmeyin'
            FROM ops.InUseExecutions e JOIN #inuse r ON r.Id=e.RecordId
            WHERE e.State IN ('Failed','Unknown','Unconfirmed','Blocked');
            INSERT #facts
            SELECT 'InUse.Partial',CONVERT(nvarchar(200),e.OperationId),'InUse',r.Id,r.Code,'InUse','Partial',
                JSON_VALUE(e.IntentJson,'$.InitiatorLabel'),r.Assignee,e.UpdatedAt,N'Kaynak eki doğrulandı; OR kapanışı doğrulanmadı. Önce mevcut işlemi uzlaştırın'
            FROM ops.InUseExecutions e JOIN #inuse r ON r.Id=e.RecordId
            WHERE e.State IN ('Failed','Unknown','Unconfirmed','Blocked')
                AND EXISTS(SELECT 1 FROM ops.InUseExecutionEvents v WHERE v.OperationId=e.OperationId
                    AND JSON_VALUE(v.EvidenceJson,'$.Step')='Attachment' AND JSON_VALUE(v.EvidenceJson,'$.Outcome')='Verified')
                AND NOT EXISTS(SELECT 1 FROM ops.InUseExecutionEvents v WHERE v.OperationId=e.OperationId
                    AND JSON_VALUE(v.EvidenceJson,'$.Step')='Closure' AND JSON_VALUE(v.EvidenceJson,'$.Outcome')='Verified');
            ;WITH evidence AS(
                SELECT e.OperationId,e.RecordId,JSON_VALUE(e.IntentJson,'$.InitiatorLabel') AS Actor,
                    JSON_VALUE(v.EvidenceJson,'$.Step') AS Step,JSON_VALUE(v.EvidenceJson,'$.Outcome') AS Outcome,
                    v.OccurredAt,ROW_NUMBER() OVER(PARTITION BY CASE WHEN JSON_VALUE(v.EvidenceJson,'$.Step')='Closure' THEN e.RecordId ELSE e.OperationId END,
                        JSON_VALUE(v.EvidenceJson,'$.Step') ORDER BY v.OccurredAt DESC,v.EventId DESC) AS n
                FROM ops.InUseExecutions e JOIN #inuse r ON r.Id=e.RecordId JOIN ops.InUseExecutionEvents v ON v.OperationId=e.OperationId
                WHERE v.OccurredAt>=@From AND v.OccurredAt<@To AND ((JSON_VALUE(v.EvidenceJson,'$.Step') IN ('Attachment','Closure') AND JSON_VALUE(v.EvidenceJson,'$.Outcome')='Verified')
                    OR (JSON_VALUE(v.EvidenceJson,'$.Step')='Bpm' AND JSON_VALUE(v.EvidenceJson,'$.Outcome') IN ('Acknowledged','Verified')))
            )
            INSERT #facts
            SELECT CASE e.Step WHEN 'Attachment' THEN 'InUse.Attachment' WHEN 'Bpm' THEN 'InUse.Bpm' ELSE 'InUse.Closed' END,
                CASE e.Step WHEN 'Closure' THEN CONVERT(nvarchar(200),r.Id) ELSE CONVERT(nvarchar(200),e.OperationId) END,
                'InUse',r.Id,r.Code,'InUse',e.Outcome,e.Actor,r.Assignee,e.OccurredAt,
                CASE e.Step WHEN 'Bpm' THEN N'İş akışı yanıtı; OR kapanışı değildir' WHEN 'Attachment' THEN N'Kaynak eki doğrulandı' ELSE N'Otoritatif OR durumu doğrulandı' END
            FROM evidence e JOIN #inuse r ON r.Id=e.RecordId
            WHERE e.n=1;
        END;

        IF @sdm=1
        BEGIN
            SELECT r.*,t.JiraIssueKey,t.SourceCloseRequested,t.ReconciliationRequired,t.CreatedByActor,t.ClosureEvidenceJson,
                (SELECT MIN(h.OccurredAt) FROM ops.OperationalRecordWorkflowHistory h WHERE h.OperationalRecordId=r.OperationalRecordId AND h.WorkflowState='JiraCreated') AS JiraCreatedAt
            INTO #sdm FROM ops.OperationalRecords r LEFT JOIN ops.JiraTransfers t ON t.OperationalRecordId=r.OperationalRecordId
            WHERE @IncludeSynthetic=1 OR r.SourceSynthetic=0;
            INSERT #facts
            SELECT v.Metric,CONVERT(nvarchar(200),r.OperationalRecordId),'Sdm',r.OperationalRecordId,r.OrCode,r.Classification,r.WorkflowState,r.CreatedByActor,NULL,r.UpdatedAt,
                CASE WHEN r.ReconciliationRequired=1 THEN N'Uzak sonucu doğrulamadan yeniden oluşturmayın'
                    WHEN r.JiraIssueKey IS NOT NULL THEN N'Jira bağlantısını koruyun; yalnız kaynak adımının sonucunu inceleyin'
                    ELSE N'Kaynak türünü, uygunluk politikasını ve eşleme engellerini inceleyin' END
            FROM #sdm r CROSS APPLY(VALUES
                ('Sdm.Eligible',CASE WHEN r.JiraEligible=1 AND r.JiraIssueKey IS NULL THEN 1 ELSE 0 END),
                ('Sdm.Blocked',CASE WHEN r.JiraEligible=0 AND r.JiraIssueKey IS NULL THEN 1 ELSE 0 END),
                ('Sdm.Unknown',CASE WHEN r.ReconciliationRequired=1 THEN 1 ELSE 0 END),
                ('Sdm.Failed',CASE WHEN r.WorkflowState IN ('JiraCreateFailed','OperationalRecordCloseFailed') AND COALESCE(r.ReconciliationRequired,0)=0 THEN 1 ELSE 0 END)) v(Metric,IncludeRow)
            WHERE v.IncludeRow=1;
            INSERT #facts
            SELECT v.Metric,CONVERT(nvarchar(200),r.OperationalRecordId),'Sdm',r.OperationalRecordId,r.OrCode,r.Classification,r.WorkflowState,r.CreatedByActor,NULL,r.JiraCreatedAt,r.JiraIssueKey
            FROM #sdm r CROSS APPLY(VALUES('Sdm.Linked',1),('Sdm.JiraOnly',CASE WHEN r.SourceCloseRequested=0 THEN 1 ELSE 0 END)) v(Metric,IncludeRow)
            WHERE r.JiraIssueKey IS NOT NULL AND r.JiraCreatedAt>=@From AND r.JiraCreatedAt<@To AND v.IncludeRow=1;
            INSERT #facts
            SELECT 'Sdm.Closed',CONVERT(nvarchar(200),r.OperationalRecordId),'Sdm',r.OperationalRecordId,r.OrCode,r.Classification,'VerifiedClosed',r.CreatedByActor,NULL,
                TRY_CONVERT(datetimeoffset,JSON_VALUE(r.ClosureEvidenceJson,'$.ObservedAt')),r.JiraIssueKey
            FROM #sdm r WHERE JSON_VALUE(r.ClosureEvidenceJson,'$.State')='VerifiedClosed'
                AND TRY_CONVERT(datetimeoffset,JSON_VALUE(r.ClosureEvidenceJson,'$.ObservedAt'))>=@From
                AND TRY_CONVERT(datetimeoffset,JSON_VALUE(r.ClosureEvidenceJson,'$.ObservedAt'))<@To;
        END;

        IF @oco=1
        BEGIN
            INSERT #facts
            SELECT 'Oco.Prepared',CONVERT(nvarchar(200),p.Id),'Oco',p.DraftId,COALESCE(JSON_VALUE(p.DocumentJson,'$.Draft.Content.OcoReference'),''),'Preparation','Prepared',p.PreparedBy,NULL,p.PreparedAt,N'Gönderim değildir'
            FROM announcements.Preparations p WHERE p.OwnerId=@owner AND p.PreparedAt>=@From AND p.PreparedAt<@To
                AND (@IncludeSynthetic=1 OR JSON_VALUE(p.DocumentJson,'$.Draft.Synthetic')='false');
            INSERT #facts
            SELECT 'Oco.Source.'+j.State,CONVERT(nvarchar(200),j.JobId),'Oco',j.DraftId,j.OcoReference,'Source',j.State,NULL,NULL,j.SubmittedAt,
                N'Kaynak önerisini ve varsa eksik alanları duyuru kaydında inceleyin'
            FROM announcements.SourceJobs j CROSS APPLY(SELECT TOP(1) DocumentJson FROM announcements.DraftRevisions d
                WHERE d.Id=j.DraftId AND d.OwnerId=@owner ORDER BY d.Version) d
            WHERE j.OwnerId=@owner AND j.SubmittedAt>=@From AND j.SubmittedAt<@To
                AND (@IncludeSynthetic=1 OR JSON_VALUE(d.DocumentJson,'$.Synthetic')='false');
            INSERT #facts
            SELECT 'Oco.'+m.Kind+'.'+m.State,CONVERT(nvarchar(200),m.CommandId),'Oco',m.DraftId,
                COALESCE(JSON_VALUE(p.DocumentJson,'$.Draft.Content.OcoReference'),''),m.Kind,m.State,p.PreparedBy,NULL,m.CreatedAt,
                CASE m.State WHEN 'Accepted' THEN N'SMTP kabulü; gelen kutusuna teslim kanıtı yok'
                    WHEN 'Unknown' THEN N'Sonucu doğrulamadan yeniden göndermeyin' ELSE N'Kayıtlı gönderim sonucunu inceleyin' END
            FROM announcements.MailCommands m JOIN announcements.Preparations p ON p.Id=m.PreparationId
            WHERE m.OwnerId=@owner AND m.CreatedAt>=@From AND m.CreatedAt<@To
                AND (@IncludeSynthetic=1 OR JSON_VALUE(p.DocumentJson,'$.Draft.Synthetic')='false');
        END;
        IF (SELECT COUNT_BIG(*) FROM #facts)>@maximum THROW 51233,'Report fact limit exceeded; narrow the period.',1;
        """;
}
