/* Reviewed offline SQL contract. Execute only through the approved DBA migration process. */
IF OBJECT_ID(N'security.Users', N'U') IS NULL
    THROW 51000, 'Prerequisite table security.Users is missing.', 1;
GO

IF COL_LENGTH(N'security.Users', N'LoginName') IS NULL
    ALTER TABLE security.Users ADD LoginName nvarchar(256) NULL;
IF COL_LENGTH(N'security.Users', N'DisplayName') IS NULL
    ALTER TABLE security.Users ADD DisplayName nvarchar(256) NULL;
IF COL_LENGTH(N'security.Users', N'Mail') IS NULL
    ALTER TABLE security.Users ADD Mail nvarchar(320) NULL;
IF COL_LENGTH(N'security.Users', N'Uid') IS NULL
    ALTER TABLE security.Users ADD Uid nvarchar(256) NULL;
IF COL_LENGTH(N'security.Users', N'ProfileUpdatedAt') IS NULL
    ALTER TABLE security.Users ADD ProfileUpdatedAt datetimeoffset(7) NULL;
GO
