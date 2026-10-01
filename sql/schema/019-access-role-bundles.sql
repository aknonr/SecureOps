SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'announcements.Preparations',N'U') IS NULL
    THROW 51000, 'Migration 018 is required.', 1;
IF COL_LENGTH(N'security.Roles',N'CapabilitiesJson') IS NOT NULL
    THROW 51000, '019 already exists; compare definitions, do not replay.', 1;
ALTER TABLE security.Roles ADD
    DisplayName nvarchar(100) NOT NULL CONSTRAINT DF_Roles_DisplayName DEFAULT(N''),
    Purpose nvarchar(500) NOT NULL CONSTRAINT DF_Roles_Purpose DEFAULT(N''),
    Version bigint NOT NULL CONSTRAINT DF_Roles_Version DEFAULT(1),
    IsProtected bit NOT NULL CONSTRAINT DF_Roles_IsProtected DEFAULT(0),
    CapabilitiesJson nvarchar(4000) NOT NULL CONSTRAINT DF_Roles_Capabilities DEFAULT(N'[]');
EXEC(N'UPDATE security.Roles SET DisplayName=RoleCode,
 Purpose=N''Preserved reviewed role'',IsProtected=CASE WHEN RoleCode=N''Admin'' THEN 1 ELSE 0 END,
 CapabilitiesJson=CASE RoleCode
 WHEN N''Admin'' THEN N''["Announcements.Drafts","InUse.View","InUse.Review","InUse.Assign","InUse.Refresh","Resources.View","Resources.Manage","Identity.Lookup","Identity.Groups.View","Identity.Groups.Members.View","Identity.PrivilegedGroups.View","Identity.Groups.Export","TeamView","AuditView","AccessAdministration","SystemDiagnostics","OperationalRecords.View","OperationalRecords.CreateJiraPreview","OperationalRecords.CreateJira","OperationalRecords.Retry","OperationalRecords.ViewDiagnostics","Access.ManageUsers","Access.ApproveRequests","Access.AssignRoles","Access.ViewAudit","Reporting.ManagementView"]''
 WHEN N''Lead'' THEN N''["Resources.View","Identity.Lookup","Identity.Groups.View","TeamView","SystemDiagnostics","OperationalRecords.View","OperationalRecords.CreateJiraPreview","OperationalRecords.CreateJira","OperationalRecords.Retry","OperationalRecords.ViewDiagnostics"]''
 WHEN N''Operator'' THEN N''["Resources.View","TeamView","OperationalRecords.View","OperationalRecords.CreateJiraPreview"]''
 WHEN N''JiraPublisher'' THEN N''["Resources.View","OperationalRecords.View","OperationalRecords.CreateJiraPreview","OperationalRecords.CreateJira","OperationalRecords.Retry"]''
 WHEN N''Auditor'' THEN N''["Resources.View","AuditView","OperationalRecords.View","OperationalRecords.ViewDiagnostics","Access.ViewAudit","Reporting.ManagementView"]''
 WHEN N''ReadOnly'' THEN N''["OperationalRecords.View","Resources.View"]''
 WHEN N''ResourceCurator'' THEN N''["Resources.View","Resources.Manage"]''
 WHEN N''InUseReviewer'' THEN N''["InUse.View","InUse.Review"]''
 WHEN N''InUseCoordinator'' THEN N''["InUse.View","InUse.Review","InUse.Assign","InUse.Refresh"]''
 ELSE N''[]'' END;');
EXEC(N'ALTER TABLE security.Roles ADD CONSTRAINT CK_Roles_Bundle CHECK
 (Version>0 AND ISJSON(CapabilitiesJson)=1 AND LEFT(CapabilitiesJson,1)=N''['');');
CREATE INDEX IX_Users_AccessPage ON security.Users(AccessStatus,UserId)
 INCLUDE(DisplayName,LoginName,Mail,AccessVersion,LastAuthenticatedAt);
CREATE INDEX IX_AccessRequests_StatusPage ON security.AccessRequests(Status,RequestedAt DESC,AccessRequestId)
 INCLUDE(UserId,Version);
COMMIT TRANSACTION;
