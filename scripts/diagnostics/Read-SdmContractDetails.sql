/* Read-only follow-up after the 022/023/024 object inventory.
   Run only in the approved TEST database. This does not read business rows,
   change session identity, or prove a migration execution history.
   Return result sets 1-5 for comparison with schema/022 and schema/023.
   After separately approved 024, the same query also reports its catalogue
   contract; an absent 024 table contributes no rows before that change.
   Result set 6 is effective ONLY for the current SQL session. It proves API
   permissions only when the approved session is the normal API SQL identity. */
SET NOCOUNT ON;

/* 1. Column types, order, nullability and defaults. OperationalRecords is
   limited to the one column added by 023. */
SELECT SCHEMA_NAME(t.schema_id) AS SchemaName, t.name AS TableName,
       c.column_id AS ColumnOrder, c.name AS ColumnName, ty.name AS SqlType,
       c.max_length AS MaxLengthBytes, c.precision AS [Precision], c.scale AS Scale,
       c.is_nullable AS IsNullable, c.is_identity AS IsIdentity,
       c.is_computed AS IsComputed, dc.definition AS DefaultDefinition
FROM sys.tables t
JOIN sys.columns c ON c.object_id = t.object_id
JOIN sys.types ty ON ty.user_type_id = c.user_type_id
LEFT JOIN sys.default_constraints dc ON dc.object_id = c.default_object_id
WHERE t.object_id IN (
    OBJECT_ID(N'ops.InUseServerReviews', N'U'),
    OBJECT_ID(N'ops.InUseExecutions', N'U'),
    OBJECT_ID(N'ops.InUseExecutionEvents', N'U'),
    OBJECT_ID(N'reporting.WorkflowSnapshots', N'U'),
    OBJECT_ID(N'reporting.WorkflowFacts', N'U'),
    OBJECT_ID(N'reporting.InUseArchiveReceipts', N'U'),
    OBJECT_ID(N'reporting.InUseReportCatalogue', N'U'))
   OR (t.object_id = OBJECT_ID(N'ops.OperationalRecords', N'U')
       AND c.name = N'SourceSynthetic')
ORDER BY SchemaName, TableName, ColumnOrder;

/* 2. PK, UQ, CHECK, FK and defaults, including trust/enabled state. */
SELECT SCHEMA_NAME(t.schema_id) AS SchemaName, t.name AS TableName,
       o.type_desc AS ConstraintKind, o.name AS ConstraintName,
       COALESCE(cc.definition, dc.definition) AS ConstraintDefinition,
       CASE WHEN o.type = 'C' THEN cc.is_disabled
            WHEN o.type = 'F' THEN fk.is_disabled END AS IsDisabled,
       CASE WHEN o.type = 'C' THEN cc.is_not_trusted
            WHEN o.type = 'F' THEN fk.is_not_trusted END AS IsNotTrusted,
       CASE WHEN o.type = 'F' THEN OBJECT_SCHEMA_NAME(fk.referenced_object_id)
            + N'.' + OBJECT_NAME(fk.referenced_object_id) END AS ReferencedTable
FROM sys.objects o
JOIN sys.tables t ON t.object_id = o.parent_object_id
LEFT JOIN sys.check_constraints cc ON cc.object_id = o.object_id
LEFT JOIN sys.foreign_keys fk ON fk.object_id = o.object_id
LEFT JOIN sys.default_constraints dc ON dc.object_id = o.object_id
WHERE o.type IN ('PK', 'UQ', 'C', 'F', 'D')
  AND t.object_id IN (
    OBJECT_ID(N'ops.InUseServerReviews', N'U'),
    OBJECT_ID(N'ops.InUseExecutions', N'U'),
    OBJECT_ID(N'ops.InUseExecutionEvents', N'U'),
    OBJECT_ID(N'reporting.WorkflowSnapshots', N'U'),
    OBJECT_ID(N'reporting.WorkflowFacts', N'U'),
    OBJECT_ID(N'reporting.InUseArchiveReceipts', N'U'),
    OBJECT_ID(N'reporting.InUseReportCatalogue', N'U'))
ORDER BY SchemaName, TableName, ConstraintKind, ConstraintName;

/* 3. Exact ordered parent/referenced columns for each FK. */
SELECT SCHEMA_NAME(t.schema_id) AS SchemaName, t.name AS TableName,
       fk.name AS ForeignKeyName, fkc.constraint_column_id AS ColumnOrder,
       pc.name AS ParentColumn, OBJECT_SCHEMA_NAME(fk.referenced_object_id)
           + N'.' + OBJECT_NAME(fk.referenced_object_id) AS ReferencedTable,
       rc.name AS ReferencedColumn
FROM sys.foreign_keys fk
JOIN sys.tables t ON t.object_id = fk.parent_object_id
JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
JOIN sys.columns pc ON pc.object_id = fk.parent_object_id
                   AND pc.column_id = fkc.parent_column_id
JOIN sys.columns rc ON rc.object_id = fk.referenced_object_id
                   AND rc.column_id = fkc.referenced_column_id
WHERE t.object_id IN (
    OBJECT_ID(N'ops.InUseServerReviews', N'U'),
    OBJECT_ID(N'ops.InUseExecutions', N'U'),
    OBJECT_ID(N'ops.InUseExecutionEvents', N'U'),
    OBJECT_ID(N'reporting.WorkflowSnapshots', N'U'),
    OBJECT_ID(N'reporting.WorkflowFacts', N'U'),
    OBJECT_ID(N'reporting.InUseArchiveReceipts', N'U'),
    OBJECT_ID(N'reporting.InUseReportCatalogue', N'U'))
ORDER BY SchemaName, TableName, ForeignKeyName, ColumnOrder;

/* 4. PK/UQ and secondary index key order, direction, filter and state. */
SELECT SCHEMA_NAME(t.schema_id) AS SchemaName, t.name AS TableName,
       i.name AS IndexName, i.type_desc AS IndexType, i.is_unique AS IsUnique,
       i.is_primary_key AS IsPrimaryKey, i.is_unique_constraint AS IsUniqueConstraint,
       i.is_disabled AS IsDisabled, i.has_filter AS HasFilter,
       i.filter_definition AS FilterDefinition, ic.key_ordinal AS KeyOrder,
       ic.is_descending_key AS IsDescending, ic.is_included_column AS IsIncluded,
       c.name AS ColumnName
FROM sys.tables t
JOIN sys.indexes i ON i.object_id = t.object_id AND i.index_id > 0
LEFT JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
LEFT JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
WHERE t.object_id IN (
    OBJECT_ID(N'ops.InUseServerReviews', N'U'),
    OBJECT_ID(N'ops.InUseExecutions', N'U'),
    OBJECT_ID(N'ops.InUseExecutionEvents', N'U'),
    OBJECT_ID(N'reporting.WorkflowSnapshots', N'U'),
    OBJECT_ID(N'reporting.WorkflowFacts', N'U'),
    OBJECT_ID(N'reporting.InUseArchiveReceipts', N'U'),
    OBJECT_ID(N'reporting.InUseReportCatalogue', N'U'))
ORDER BY SchemaName, TableName, IndexName, KeyOrder, IsIncluded, ColumnName;

/* 5. Trigger definition and enabled/INSTEAD OF status; no source rows. */
SELECT SCHEMA_NAME(t.schema_id) AS SchemaName, t.name AS TableName,
       tr.name AS TriggerName, tr.is_disabled AS IsDisabled,
       tr.is_instead_of_trigger AS IsInsteadOf,
       OBJECT_DEFINITION(tr.object_id) AS TriggerDefinition
FROM sys.tables t
JOIN sys.triggers tr ON tr.parent_id = t.object_id
WHERE t.object_id IN (
    OBJECT_ID(N'ops.InUseServerReviews', N'U'),
    OBJECT_ID(N'ops.InUseExecutions', N'U'),
    OBJECT_ID(N'ops.InUseExecutionEvents', N'U'),
    OBJECT_ID(N'reporting.WorkflowSnapshots', N'U'),
    OBJECT_ID(N'reporting.WorkflowFacts', N'U'),
    OBJECT_ID(N'reporting.InUseArchiveReceipts', N'U'),
    OBJECT_ID(N'reporting.InUseReportCatalogue', N'U'))
ORDER BY SchemaName, TableName, TriggerName;

/* 6. Current-session effective rights; not API rights in an operator session.
   Retain login/user names privately; do not publish them or credentials. */
SELECT DB_NAME() AS DatabaseName, ORIGINAL_LOGIN() AS SessionOriginalLogin,
       USER_NAME() AS SessionDatabaseUser,
       v.ObjectName, v.ExpectedRights,
       HAS_PERMS_BY_NAME(v.ObjectName, 'OBJECT', 'SELECT') AS CanSelect,
       HAS_PERMS_BY_NAME(v.ObjectName, 'OBJECT', 'INSERT') AS CanInsert,
       HAS_PERMS_BY_NAME(v.ObjectName, 'OBJECT', 'UPDATE') AS CanUpdate,
       HAS_PERMS_BY_NAME(v.ObjectName, 'OBJECT', 'DELETE') AS CanDelete,
       HAS_PERMS_BY_NAME(v.ObjectName, 'OBJECT', 'ALTER') AS CanAlter,
       HAS_PERMS_BY_NAME(v.ObjectName, 'OBJECT', 'CONTROL') AS CanControl
FROM (VALUES
    (N'ops.InUseServerReviews', N'SELECT, INSERT'),
    (N'ops.InUseExecutions', N'SELECT, INSERT, UPDATE'),
    (N'ops.InUseExecutionEvents', N'SELECT, INSERT'),
    (N'reporting.WorkflowSnapshots', N'SELECT, INSERT'),
    (N'reporting.WorkflowFacts', N'SELECT, INSERT'),
    (N'reporting.InUseArchiveReceipts', N'SELECT, INSERT'),
    (N'reporting.InUseReportCatalogue', N'SELECT, INSERT after approved 024')
) AS v(ObjectName, ExpectedRights)
ORDER BY v.ObjectName;
