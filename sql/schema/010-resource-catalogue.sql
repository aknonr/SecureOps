/* Additive, offline-reviewed contract. Never run by application startup. */
SET XACT_ABORT ON;
GO
IF COL_LENGTH('ops.OperationalRecords', 'SdmEvaluationJson') IS NULL
    THROW 51010, 'Migration 009 is required before 010.', 1;
GO
IF SCHEMA_ID('resources') IS NULL EXEC('CREATE SCHEMA resources');
GO
BEGIN TRANSACTION;
IF OBJECT_ID('resources.Categories', 'U') IS NULL
BEGIN
    CREATE TABLE resources.Categories
    (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_ResourceCategories PRIMARY KEY,
        Name nvarchar(80) NOT NULL,
        DisplayOrder int NOT NULL CONSTRAINT DF_ResourceCategories_Order DEFAULT 0,
        ManagersOnly bit NOT NULL CONSTRAINT DF_ResourceCategories_Managers DEFAULT 0,
        Archived bit NOT NULL CONSTRAINT DF_ResourceCategories_Archived DEFAULT 0,
        Version bigint NOT NULL CONSTRAINT DF_ResourceCategories_Version DEFAULT 1,
        UpdatedAt datetimeoffset(7) NOT NULL,
        CONSTRAINT CK_ResourceCategories_Name CHECK (LEN(LTRIM(RTRIM(Name))) > 0),
        CONSTRAINT CK_ResourceCategories_Order CHECK (DisplayOrder BETWEEN 0 AND 100000),
        CONSTRAINT CK_ResourceCategories_Version CHECK (Version > 0)
    );
    CREATE INDEX IX_ResourceCategories_Visibility ON resources.Categories(Archived, ManagersOnly, DisplayOrder, Id);
END;
IF OBJECT_ID('resources.Links', 'U') IS NULL
BEGIN
    CREATE TABLE resources.Links
    (
        Id uniqueidentifier NOT NULL CONSTRAINT PK_ResourceLinks PRIMARY KEY,
        CategoryId uniqueidentifier NOT NULL CONSTRAINT FK_ResourceLinks_Category REFERENCES resources.Categories(Id),
        Name nvarchar(120) NOT NULL,
        Url nvarchar(2048) NOT NULL,
        Purpose nvarchar(300) NOT NULL,
        Notes nvarchar(1000) NULL,
        Environment nvarchar(40) NULL,
        Location nvarchar(40) NULL,
        TagsJson nvarchar(2048) NOT NULL CONSTRAINT DF_ResourceLinks_Tags DEFAULT N'[]',
        DisplayOrder int NOT NULL CONSTRAINT DF_ResourceLinks_Order DEFAULT 0,
        Active bit NOT NULL CONSTRAINT DF_ResourceLinks_Active DEFAULT 1,
        Archived bit NOT NULL CONSTRAINT DF_ResourceLinks_Archived DEFAULT 0,
        Version bigint NOT NULL CONSTRAINT DF_ResourceLinks_Version DEFAULT 1,
        UpdatedAt datetimeoffset(7) NOT NULL,
        CONSTRAINT CK_ResourceLinks_Content CHECK (LEN(LTRIM(RTRIM(Name))) > 0 AND LEN(LTRIM(RTRIM(Purpose))) > 0),
        CONSTRAINT CK_ResourceLinks_Https CHECK (LEFT(Url, 8) COLLATE Latin1_General_100_BIN2 = N'https://'),
        CONSTRAINT CK_ResourceLinks_Tags CHECK (ISJSON(TagsJson) = 1 AND LEFT(TagsJson, 1) = N'['),
        CONSTRAINT CK_ResourceLinks_Order CHECK (DisplayOrder BETWEEN 0 AND 100000),
        CONSTRAINT CK_ResourceLinks_Version CHECK (Version > 0)
    );
    CREATE INDEX IX_ResourceLinks_CategoryStateOrder ON resources.Links(CategoryId, Archived, Active, DisplayOrder, Id);
    CREATE INDEX IX_ResourceLinks_EnvironmentLocation ON resources.Links(Environment, Location) INCLUDE(CategoryId, Archived, Active);
END;
IF OBJECT_ID('resources.PersonalPreferences', 'U') IS NULL
BEGIN
    CREATE TABLE resources.PersonalPreferences
    (
        UserId uniqueidentifier NOT NULL CONSTRAINT PK_ResourcePreferences PRIMARY KEY
            CONSTRAINT FK_ResourcePreferences_User REFERENCES security.Users(UserId),
        Version bigint NOT NULL,
        PreferencesJson nvarchar(max) NOT NULL,
        UpdatedAt datetimeoffset(7) NOT NULL,
        CONSTRAINT CK_ResourcePreferences_Version CHECK (Version > 0),
        CONSTRAINT CK_ResourcePreferences_Json CHECK (ISJSON(PreferencesJson) = 1 AND DATALENGTH(PreferencesJson) <= 240000),
        CONSTRAINT CK_ResourcePreferences_Shape CHECK (
            JSON_QUERY(PreferencesJson, '$.FavouriteIds') IS NOT NULL AND LEFT(JSON_QUERY(PreferencesJson, '$.FavouriteIds'), 1) = N'[' AND
            JSON_QUERY(PreferencesJson, '$.Sets') IS NOT NULL AND LEFT(JSON_QUERY(PreferencesJson, '$.Sets'), 1) = N'[' AND
            JSON_VALUE(PreferencesJson, '$.Version') IS NOT NULL AND
            TRY_CONVERT(bigint, JSON_VALUE(PreferencesJson, '$.Version')) IS NOT NULL AND
            TRY_CONVERT(bigint, JSON_VALUE(PreferencesJson, '$.Version')) = Version)
    );
END;
IF EXISTS (SELECT 1 FROM security.Roles WHERE RoleId = 7 AND RoleCode <> N'ResourceCurator')
    THROW 51011, 'ResourceCurator role ID collision; DBA review required.', 1;
IF NOT EXISTS (SELECT 1 FROM security.Roles WHERE RoleCode = N'ResourceCurator')
    INSERT INTO security.Roles(RoleId, RoleCode) VALUES(7, N'ResourceCurator');
COMMIT TRANSACTION;
GO
