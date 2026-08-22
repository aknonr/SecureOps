/* Preserve unknown source creation time as NULL instead of fabricated data. */
IF OBJECT_ID(N'ops.OperationalRecords', N'U') IS NULL
    THROW 51000, 'Prerequisite table ops.OperationalRecords is missing.', 1;
GO

IF EXISTS
(
    SELECT 1
    FROM sys.columns
    WHERE object_id = OBJECT_ID(N'ops.OperationalRecords')
      AND name = N'SourceCreatedAt'
      AND is_nullable = 0
)
    ALTER TABLE ops.OperationalRecords ALTER COLUMN SourceCreatedAt datetimeoffset(7) NULL;
GO
