/* Read-only 022/023 permission probe, NOT an execution/activation command.
   Execute only through a separately approved normal API-process diagnostic using
   its existing SecureOpsDb connection and unchanged startup configuration.
   No EXECUTE AS, credentials, grant changes, write probes or corporate records.
   Operator/sqlcmd output proves that session only, not the API's effective rights.
   rc6.26 and the sealed deda848 candidate have no endpoint for this probe.
   024 is intentionally excluded: its scoped metadata remains NotVisibleOrAbsent. */
SELECT CONVERT(nvarchar(128), SERVERPROPERTY('ServerName')) AS ServerName,
       DB_NAME() AS DatabaseName, SYSUTCDATETIME() AS CapturedAtUtc,
       @@SPID AS SessionId, ORIGINAL_LOGIN() AS OriginalLogin,
       SUSER_SNAME() AS EffectiveLogin, USER_NAME() AS DatabaseUser,
       APP_NAME() AS ApplicationName;

SELECT required.ObjectName, required.PermissionName,
       HAS_PERMS_BY_NAME(required.ObjectName, 'OBJECT', required.PermissionName)
           AS HasEffectivePermission
FROM (VALUES
    (N'ops.InUseServerReviews', N'SELECT'),
    (N'ops.InUseServerReviews', N'INSERT'),
    (N'ops.InUseExecutions', N'SELECT'),
    (N'ops.InUseExecutions', N'INSERT'),
    (N'ops.InUseExecutions', N'UPDATE'),
    (N'ops.InUseExecutionEvents', N'SELECT'),
    (N'ops.InUseExecutionEvents', N'INSERT'),
    (N'reporting.WorkflowSnapshots', N'SELECT'),
    (N'reporting.WorkflowSnapshots', N'INSERT'),
    (N'reporting.WorkflowFacts', N'SELECT'),
    (N'reporting.WorkflowFacts', N'INSERT'),
    (N'reporting.InUseArchiveReceipts', N'SELECT'),
    (N'reporting.InUseArchiveReceipts', N'INSERT')
) AS required(ObjectName, PermissionName)
ORDER BY required.ObjectName, required.PermissionName;
/* 1 = allowed; 0 = denied; NULL = unresolved/invalid metadata, never allowed.
   Permission metadata is not proof that a business write succeeds, nor proof
   of ownership chaining, trigger behavior or corporate workflow acceptance. */
