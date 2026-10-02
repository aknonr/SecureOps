using Dapper;
using FluentAssertions;
using Microsoft.Data.SqlClient;

namespace SecureOps.Tests.Integration.ServiceAccounts;

/// <summary>Local SQL execution-token checks, not Windows-principal or corporate acceptance.</summary>
public sealed class ServiceAccountRoleSqlTests
{
    [ServiceAccountSqlFact]
    public async Task ApiRole_AllRequiredVerbsAreAllowed_AndUnneededPrivilegesAreDenied()
    {
        await ProbeAsync("svcacct_api_runtime", async (connection, transaction) =>
        {
            string[] mutable = ["Organizations", "Teams", "People", "ScopeGrants", "Accounts", "OwnershipAssignments",
                "Handovers", "WorkRequests", "ActionEvents", "Findings", "IdentityTransitions", "ImportBatches", "ReminderOutbox"];
            string[] append = ["Communications", "PersonAliases", "AccountAliases", "ExternalRecords", "ExternalRecordLinks",
                "CommunicationAccounts", "AccountObservations", "Evidence", "ReportSnapshots", "History"];
            foreach (string table in mutable)
            {
                await RightsAsync(connection, transaction, "svcacct." + table, true, "SELECT", "INSERT", "UPDATE");
            }
            foreach (string table in append)
            {
                await RightsAsync(connection, transaction, "svcacct." + table, true, "SELECT", "INSERT");
                await RightsAsync(connection, transaction, "svcacct." + table, false, "UPDATE", "DELETE");
            }
            await RightsAsync(connection, transaction, "svcacct.ImportRows", true, "SELECT", "INSERT", "DELETE");
            await RightsAsync(connection, transaction, "security.Users", true, "SELECT");
            await RightsAsync(connection, transaction, "audit.AuditLog", true, "INSERT");
            await RightsAsync(connection, transaction, "audit.AuditLog", false, "SELECT", "UPDATE", "DELETE");
            await RightsAsync(connection, transaction, "svcacct.Accounts", false, "DELETE", "ALTER", "CONTROL");
            await RightsAsync(connection, transaction, "svcacct.TeamMemberships", false, "SELECT", "INSERT", "UPDATE", "DELETE");
            // Numbered 026 retains SA-002: usages and team roles are mutable but never deleted.
            foreach (string table in new[] { "AccountUsages", "TeamRoles" })
            {
                await RightsAsync(connection, transaction, "svcacct." + table, true, "SELECT", "INSERT", "UPDATE");
                await RightsAsync(connection, transaction, "svcacct." + table, false, "DELETE", "ALTER", "CONTROL");
                string column = table == "AccountUsages" ? "Notes" : "Reason";
                await connection.ExecuteAsync($"SELECT TOP (0) Id FROM svcacct.{table}; UPDATE svcacct.{table} SET {column} = {column} WHERE 1 = 0;",
                    transaction: transaction);
                Func<Task> deleteNewTable = async () => await connection.ExecuteAsync($"DELETE FROM svcacct.{table} WHERE 1 = 0;", transaction: transaction);
                (await deleteNewTable.Should().ThrowAsync<SqlException>()).Which.Number.Should().Be(229);
            }
            await connection.ExecuteAsync("SELECT TOP (0) Id FROM svcacct.Accounts; UPDATE svcacct.Accounts SET Notes = Notes WHERE 1 = 0;",
                transaction: transaction);
            Func<Task> delete = async () => await connection.ExecuteAsync("DELETE FROM svcacct.Accounts WHERE 1 = 0;", transaction: transaction);
            (await delete.Should().ThrowAsync<SqlException>()).Which.Number.Should().Be(229);
        });
    }

    [ServiceAccountSqlFact]
    public async Task WorkerRole_OnlyReminderOperationsAreAllowed()
    {
        await ProbeAsync("svcacct_worker_runtime", async (connection, transaction) =>
        {
            await RightsAsync(connection, transaction, "svcacct.Accounts", true, "SELECT");
            await RightsAsync(connection, transaction, "svcacct.WorkRequests", true, "SELECT");
            await RightsAsync(connection, transaction, "svcacct.ReminderOutbox", true, "SELECT", "INSERT", "UPDATE");
            await RightsAsync(connection, transaction, "svcacct.Accounts", false, "INSERT", "UPDATE", "DELETE", "ALTER");
            await RightsAsync(connection, transaction, "svcacct.WorkRequests", false, "INSERT", "UPDATE", "DELETE");
            await RightsAsync(connection, transaction, "svcacct.History", false, "SELECT", "INSERT", "UPDATE", "DELETE");
            await RightsAsync(connection, transaction, "svcacct.Evidence", false, "SELECT", "INSERT");
            await RightsAsync(connection, transaction, "audit.AuditLog", false, "INSERT");
            await RightsAsync(connection, transaction, "svcacct.AccountUsages", false, "SELECT", "INSERT", "UPDATE", "DELETE", "ALTER", "CONTROL");
            await RightsAsync(connection, transaction, "svcacct.TeamRoles", false, "SELECT", "INSERT", "UPDATE", "DELETE", "ALTER", "CONTROL");
            await connection.ExecuteAsync("SELECT TOP (0) Id FROM svcacct.Accounts; SELECT TOP (0) Id FROM svcacct.WorkRequests; "
                + "UPDATE svcacct.ReminderOutbox SET LeaseOwner = LeaseOwner WHERE 1 = 0;", transaction: transaction);
            Func<Task> readHistory = async () => await connection.ExecuteAsync("SELECT TOP (0) Id FROM svcacct.History;", transaction: transaction);
            (await readHistory.Should().ThrowAsync<SqlException>()).Which.Number.Should().Be(229);
        });
    }

    private static async Task RightsAsync(SqlConnection connection, System.Data.IDbTransaction transaction,
        string objectName, bool expected, params string[] permissions)
    {
        foreach (string permission in permissions)
        {
            int? allowed = await connection.QuerySingleAsync<int?>(
                "SELECT HAS_PERMS_BY_NAME(@objectName, 'OBJECT', @permission);", new { objectName, permission }, transaction);
            allowed.Should().Be(expected ? 1 : 0, $"{objectName} {permission}");
        }
    }

    private static async Task ProbeAsync(string role, Func<SqlConnection, System.Data.IDbTransaction, Task> check)
    {
        SqlConnectionStringBuilder settings = new(Environment.GetEnvironmentVariable(ServiceAccountSqlFactAttribute.Variable));
        settings.DataSource.Should().BeEquivalentTo("(localdb)\\SecureOpsResourcesV1", "only the approved disposable local instance");
        settings.InitialCatalog.Should().StartWith("SecureOps_Sa", "never the corporate database");
        settings.IntegratedSecurity.Should().BeTrue();
        await using SqlConnection connection = new(settings.ConnectionString);
        await connection.OpenAsync();
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
        string user = "sa_role_probe_" + Guid.NewGuid().ToString("N");
        bool impersonated = false;
        try
        {
            // Both identifiers are test-owned (a fixed role and a generated GUID), never caller input.
            await connection.ExecuteAsync($"CREATE USER [{user}] WITHOUT LOGIN; ALTER ROLE [{role}] ADD MEMBER [{user}];",
                transaction: transaction);
            await connection.ExecuteAsync($"EXECUTE AS USER = N'{user}';", transaction: transaction);
            impersonated = true;
            (await connection.QuerySingleAsync<string>("SELECT USER_NAME();", transaction: transaction)).Should().Be(user);
            (await connection.QuerySingleAsync<int?>("SELECT HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'CONTROL');", transaction: transaction))
                .Should().Be(0);
            await check(connection, transaction);
        }
        finally
        {
            if (impersonated)
            {
                await connection.ExecuteAsync("REVERT;", transaction: transaction);
            }
            await transaction.RollbackAsync();
        }
    }
}
