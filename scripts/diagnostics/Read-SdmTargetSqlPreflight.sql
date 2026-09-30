/* Read-only TEST preflight. Run in the DBA-approved database context.
   No repository migration ledger exists. Object visibility is not a DBA install receipt. */
SET NOCOUNT ON;

SELECT CONVERT(nvarchar(256), SERVERPROPERTY('ServerName')) AS ServerName,
       DB_NAME() AS DatabaseName,
       DB_ID() AS DatabaseId,
       SYSUTCDATETIME() AS CapturedAtUtc,
       HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'VIEW DEFINITION') AS CanViewDatabaseDefinition,
       N'No repository ledger; external DBA record not verified by this query' AS MigrationRecordStatus;

WITH Expected(Migration, Kind, SchemaName, ObjectName, ParentName) AS (
    SELECT * FROM (VALUES
        ('022','TABLE','ops','InUseServerReviews',NULL),
        ('022','TABLE','ops','InUseExecutions',NULL),
        ('022','TABLE','ops','InUseExecutionEvents',NULL),
        ('022','INDEX','ops','IX_InUseServerReviews_Identity','ops.InUseServerReviews'),
        ('022','INDEX','ops','UX_InUseExecutions_Active','ops.InUseExecutions'),
        ('022','INDEX','ops','IX_InUseExecutions_Recovery','ops.InUseExecutions'),
        ('022','TRIGGER','ops','tr_InUseServerReviews_AppendOnly',NULL),
        ('022','TRIGGER','ops','tr_InUseExecutionEvents_AppendOnly',NULL),
        ('022','TRIGGER','ops','tr_InUseExecutions_ImmutableIntent',NULL),
        ('023','COLUMN','ops','SourceSynthetic','ops.OperationalRecords'),
        ('023','TABLE','reporting','WorkflowSnapshots',NULL),
        ('023','TABLE','reporting','WorkflowFacts',NULL),
        ('023','TABLE','reporting','InUseArchiveReceipts',NULL),
        ('023','INDEX','reporting','IX_WorkflowSnapshots_Owner','reporting.WorkflowSnapshots'),
        ('023','INDEX','reporting','IX_WorkflowFacts_Filter','reporting.WorkflowFacts'),
        ('023','INDEX','reporting','IX_InUseArchiveReceipts_Time','reporting.InUseArchiveReceipts'),
        ('023','TRIGGER','reporting','TR_WorkflowSnapshots_Immutable',NULL),
        ('023','TRIGGER','reporting','TR_WorkflowFacts_Immutable',NULL),
        ('023','TRIGGER','reporting','TR_InUseArchiveReceipts_Immutable',NULL),
        ('024','TABLE','reporting','InUseReportCatalogue',NULL),
        ('024','INDEX','reporting','IX_InUseReportCatalogue_Code','reporting.InUseReportCatalogue'),
        ('024','TRIGGER','reporting','TR_InUseReportCatalogue_Immutable',NULL),
        ('024','FOREIGN_KEY','reporting','FK_InUseReportCatalogue_Receipt','reporting.InUseReportCatalogue')
    ) AS V(Migration, Kind, SchemaName, ObjectName, ParentName)
)
SELECT e.Migration, e.Kind, e.SchemaName, e.ObjectName, e.ParentName,
       CASE
           WHEN e.Kind = 'TABLE' AND OBJECT_ID(e.SchemaName + N'.' + e.ObjectName, 'U') IS NOT NULL THEN 'Visible'
           WHEN e.Kind = 'TRIGGER' AND tr.object_id IS NOT NULL THEN 'Visible'
           WHEN e.Kind = 'INDEX' AND ix.index_id IS NOT NULL THEN 'Visible'
           WHEN e.Kind = 'COLUMN' AND col.column_id IS NOT NULL THEN 'Visible'
           WHEN e.Kind = 'FOREIGN_KEY' AND fk.object_id IS NOT NULL THEN 'Visible'
           ELSE 'NotVisibleOrAbsent'
       END AS MetadataState,
       CASE WHEN e.Kind = 'TRIGGER' THEN tr.is_disabled
            WHEN e.Kind = 'INDEX' THEN ix.is_disabled
            WHEN e.Kind = 'FOREIGN_KEY' THEN fk.is_disabled END AS IsDisabled,
       CASE WHEN e.Kind = 'INDEX' THEN ix.is_unique END AS IsUnique,
       CASE WHEN e.Kind = 'INDEX' THEN ix.filter_definition END AS FilterDefinition,
       CASE WHEN e.Kind = 'COLUMN' THEN ty.name END AS ColumnType,
       CASE WHEN e.Kind = 'COLUMN' THEN col.is_nullable END AS ColumnIsNullable
FROM Expected e
LEFT JOIN sys.triggers tr ON e.Kind = 'TRIGGER'
    AND tr.object_id = OBJECT_ID(e.SchemaName + N'.' + e.ObjectName, 'TR')
LEFT JOIN sys.indexes ix ON e.Kind = 'INDEX'
    AND ix.object_id = OBJECT_ID(e.ParentName, 'U') AND ix.name = e.ObjectName
LEFT JOIN sys.columns col ON e.Kind = 'COLUMN'
    AND col.object_id = OBJECT_ID(e.ParentName, 'U') AND col.name = e.ObjectName
LEFT JOIN sys.types ty ON ty.user_type_id = col.user_type_id
LEFT JOIN sys.foreign_keys fk ON e.Kind = 'FOREIGN_KEY'
    AND fk.parent_object_id = OBJECT_ID(e.ParentName, 'U') AND fk.name = e.ObjectName
ORDER BY e.Migration, e.Kind, e.SchemaName, e.ObjectName;

/* Discovery only: a matching name is not proof that a table is a migration ledger.
   No unknown table's data is read. The DBA must supply its signed execution record. */
SELECT s.name AS PossibleLedgerSchema, t.name AS PossibleLedgerTable,
       c.name AS ColumnName, ty.name AS ColumnType
FROM sys.tables t
JOIN sys.schemas s ON s.schema_id = t.schema_id
JOIN sys.columns c ON c.object_id = t.object_id
JOIN sys.types ty ON ty.user_type_id = c.user_type_id
WHERE LOWER(t.name) LIKE '%migrat%' OR LOWER(t.name) LIKE '%deploy%'
   OR LOWER(t.name) LIKE '%schemaversion%'
ORDER BY s.name, t.name, c.column_id;
