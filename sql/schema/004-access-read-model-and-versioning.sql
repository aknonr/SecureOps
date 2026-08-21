/* Reviewed offline access-control upgrade. Execute only through the approved DBA migration process. */
IF COL_LENGTH(N'security.Users', N'AccessVersion') IS NULL
    ALTER TABLE security.Users ADD AccessVersion bigint NOT NULL
        CONSTRAINT DF_SecurityUsers_AccessVersion DEFAULT 1;
IF COL_LENGTH(N'security.AccessRequests', N'Version') IS NULL
    ALTER TABLE security.AccessRequests ADD Version bigint NOT NULL
        CONSTRAINT DF_SecurityAccessRequests_Version DEFAULT 1;
GO
