using System.Data;
using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using SecureOps.Domain.ServiceAccounts;
using SecureOps.Infrastructure.Audit;

namespace SecureOps.Infrastructure.ServiceAccounts;

/// <summary>
/// Dapper/SqlClient persistence for the svcacct candidate schema. Every mutation checks rowversion,
/// and writes svcacct.History plus audit.AuditLog in the same SQL transaction. All SQL is parameterized;
/// the only interpolated fragments are closed internal constants.
/// </summary>
public sealed partial class SqlServiceAccountRepository
{
    private const int _timeoutSeconds = 30;
    private const int _commitTimeoutSeconds = 180;

    /// <summary>
    /// Application lock the import commit holds exclusively for its whole serializable transaction. Every other module
    /// write that touches tables the commit reads or writes takes it shared as its first statement, so a write never holds
    /// row locks inside the commit's range while the commit waits for them (the recorded 1205 deadlock).
    /// </summary>
    private const string _importCommitLock = "svcacct:import-commit";

    /// <summary>Shared gate wait; below the 30 s command timeout so a long commit surfaces as error 51312, not a timeout.</summary>
    private const int _writeGateTimeoutMilliseconds = 25000;
    private readonly string _connectionString;

    /// <summary>Scope predicate over alias <c>a</c> (svcacct.Accounts); mirrors <see cref="ServiceAccountScope.Covers"/>.</summary>
    internal const string ScopePredicate = """
        (@ScopeAll = 1
         OR a.ReportOrganizationId IN (SELECT CONVERT(uniqueidentifier, value) FROM OPENJSON(@ScopeOrgs))
         OR a.CurrentOwnerTeamId IN (SELECT CONVERT(uniqueidentifier, value) FROM OPENJSON(@ScopeTeams))
         OR EXISTS (SELECT 1 FROM svcacct.WorkRequests sr WHERE sr.AccountId = a.Id AND sr.Status = 'Open'
                    AND sr.TargetTeamId IN (SELECT CONVERT(uniqueidentifier, value) FROM OPENJSON(@ScopeTeams)))
         OR EXISTS (SELECT 1 FROM svcacct.Handovers sh WHERE sh.AccountId = a.Id AND sh.Status IN ('Proposed','Accepted')
                    AND sh.TargetTeamId IN (SELECT CONVERT(uniqueidentifier, value) FROM OPENJSON(@ScopeTeams))))
        """;

    static SqlServiceAccountRepository()
    {
        SqlMapper.AddTypeHandler(new DateOnlyHandler());
    }

    /// <summary>Initializes the repository from <c>ConnectionStrings:SecureOpsDb</c>.</summary>
    public SqlServiceAccountRepository(IConfiguration configuration)
    {
        _connectionString = ModuleConnectionString(configuration.GetConnectionString(AuditConnectionStrings.SecureOpsDb)
            ?? throw new InvalidOperationException("ConnectionStrings:SecureOpsDb is required for Service Accounts SQL persistence."));
    }

    /// <summary>Suffix that gives the module its own connection pool and a recognisable SQL program name.</summary>
    public const string ApplicationNameSuffix = " / Service Accounts";

    /// <summary>
    /// The configured connection with a module-specific <c>Application Name</c>. SqlClient pools by connection string and a
    /// pooled session keeps the isolation level of its last transaction, so the module's SERIALIZABLE transactions must not
    /// share a pool with platform code (whose autocommit reads and READPAST queries would silently inherit it).
    /// </summary>
    public static string ModuleConnectionString(string configured)
    {
        SqlConnectionStringBuilder builder = new(configured);
        string current = builder.ShouldSerialize("Application Name") && !string.IsNullOrWhiteSpace(builder.ApplicationName) ? builder.ApplicationName : "SecureOps";
        if (!current.EndsWith(ApplicationNameSuffix, StringComparison.Ordinal))
        {
            builder.ApplicationName = (current.Length + ApplicationNameSuffix.Length > 128 ? current[..(128 - ApplicationNameSuffix.Length)] : current)
                + ApplicationNameSuffix;
        }

        return builder.ConnectionString;
    }

    /// <summary>Loads the caller's active grants and the organization/team tree.</summary>
    public async Task<ScopeData> LoadScopeDataAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await OpenAsync(cancellationToken);
        using SqlMapper.GridReader grid = await connection.QueryMultipleAsync(Cmd("""
            SELECT ScopeKind, OrganizationId, TeamId FROM svcacct.ScopeGrants WHERE UserId = @userId AND RevokedAt IS NULL;
            SELECT Id, ParentId FROM svcacct.Organizations;
            SELECT Id, OrganizationId FROM svcacct.Teams;
            """, new { userId }, null, cancellationToken));
        ScopeGrant[] grants = [.. (await grid.ReadAsync<(string Kind, Guid? OrganizationId, Guid? TeamId)>())
            .Select(g => new ScopeGrant(Enum.Parse<ScopeKind>(g.Kind), g.OrganizationId, g.TeamId))];
        OrganizationNode[] orgs = [.. (await grid.ReadAsync<(Guid Id, Guid? ParentId)>()).Select(o => new OrganizationNode(o.Id, o.ParentId))];
        TeamNode[] teams = [.. (await grid.ReadAsync<(Guid Id, Guid? OrganizationId)>()).Select(t => new TeamNode(t.Id, t.OrganizationId))];
        return new ScopeData(grants, orgs, teams);
    }

    /// <summary>Returns scope parameters for SQL predicates.</summary>
    internal static DynamicParameters ScopeParameters(ServiceAccountScope scope, object? values = null)
    {
        DynamicParameters parameters = new(values);
        parameters.Add("ScopeAll", scope.All);
        parameters.Add("ScopeOrgs", JsonSerializer.Serialize(scope.Organizations.Select(o => o.ToString("D"))));
        parameters.Add("ScopeTeams", JsonSerializer.Serialize(scope.Teams.Select(t => t.ToString("D"))));
        return parameters;
    }

    private async Task<SqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        SqlConnection connection = new(_connectionString);
        await connection.OpenAsync(cancellationToken);
        // A pooled session may still carry SERIALIZABLE from the module's previous transaction; module reads outside a
        // transaction always run at the platform default.
        await using (SqlCommand reset = new("SET TRANSACTION ISOLATION LEVEL READ COMMITTED;", connection))
        {
            await reset.ExecuteNonQueryAsync(cancellationToken);
        }

        return connection;
    }

    private static CommandDefinition Cmd(string sql, object? parameters, IDbTransaction? transaction, CancellationToken cancellationToken,
        int timeout = _timeoutSeconds) => new(sql, parameters, transaction, timeout, cancellationToken: cancellationToken);

    /// <summary>Encodes a SQL rowversion for clients.</summary>
    internal static string Version(byte[]? rowVersion) => rowVersion is null ? string.Empty : Convert.ToBase64String(rowVersion);

    /// <summary>Decodes a client version; invalid input never matches.</summary>
    internal static byte[] Version(string? value)
    {
        try
        {
            return value is { Length: 12 } ? Convert.FromBase64String(value) : [];
        }
        catch (FormatException)
        {
            return [];
        }
    }

    private static async Task HistoryAsync(SqlConnection connection, SqlTransaction transaction, string entityType, Guid entityId, Guid? accountId,
        string action, object? changes, string? reason, SaActor actor, DateTimeOffset at, CancellationToken cancellationToken)
    {
        await connection.ExecuteAsync(Cmd("""
            INSERT INTO svcacct.History(EntityType, EntityId, AccountId, Action, ChangesJson, Reason, ActorUserId, CorrelationId, OccurredAt)
            VALUES(@entityType, @entityId, @accountId, @action, @changes, @reason, @UserId, @CorrelationId, @at);
            """, new
        {
            entityType,
            entityId,
            accountId,
            action,
            changes = changes is null ? null : JsonSerializer.Serialize(changes),
            reason,
            actor.UserId,
            CorrelationId = Truncate(actor.CorrelationId, 128),
            at
        }, transaction, cancellationToken));
    }

    /// <summary>Platform audit: safe identifiers only (no names, notes or values).</summary>
    private static async Task AuditAsync(SqlConnection connection, SqlTransaction transaction, string action, object details, SaActor actor,
        DateTimeOffset at, CancellationToken cancellationToken)
    {
        await connection.ExecuteAsync(Cmd("""
            INSERT INTO audit.AuditLog(OccurredAt, Actor, Action, CorrelationId, DetailsJson)
            VALUES(@at, @actor, @action, @CorrelationId, @details);
            """, new
        {
            at,
            actor = actor.UserId.ToString("D"),
            action = "ServiceAccount." + action,
            CorrelationId = Truncate(actor.CorrelationId, 256),
            details = JsonSerializer.Serialize(details)
        }, transaction, cancellationToken));
    }

    private static async Task<SqlTransaction> BeginAsync(SqlConnection connection, CancellationToken cancellationToken,
        IsolationLevel isolation = IsolationLevel.ReadCommitted) =>
        (SqlTransaction)await connection.BeginTransactionAsync(isolation, cancellationToken);

    /// <summary>
    /// Begins a module write transaction and waits (shared) behind any running import commit before any row is locked.
    /// A gate timeout throws SQL error 51312 and is reported as persistence unavailable with nothing written.
    /// </summary>
    private static async Task<SqlTransaction> BeginWriteAsync(SqlConnection connection, CancellationToken cancellationToken,
        IsolationLevel isolation = IsolationLevel.ReadCommitted)
    {
        SqlTransaction transaction = await BeginAsync(connection, cancellationToken, isolation);
        await connection.ExecuteAsync(Cmd("""
            DECLARE @result int;
            EXEC @result = sp_getapplock @Resource = @resource, @LockMode = 'Shared', @LockOwner = 'Transaction', @LockTimeout = @timeout;
            IF @result < 0 THROW 51312, 'Service Accounts import commit in progress.', 1;
            """, new { resource = _importCommitLock, timeout = _writeGateTimeoutMilliseconds }, transaction, cancellationToken));
        return transaction;
    }

    private static string? Truncate(string? value, int length) => value is null || value.Length <= length ? value : value[..length];

    private sealed class DateOnlyHandler : SqlMapper.TypeHandler<DateOnly>
    {
        public override DateOnly Parse(object value) => value is DateOnly d ? d : DateOnly.FromDateTime((DateTime)value);

        public override void SetValue(IDbDataParameter parameter, DateOnly value)
        {
            parameter.DbType = DbType.Date;
            parameter.Value = value.ToDateTime(TimeOnly.MinValue);
        }
    }
}
