using System.Data;
using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using SecureOps.Infrastructure.Audit;
using SecureOps.Shared.Audit;

namespace SecureOps.Infrastructure.Access;

/// <summary>SQL Server implementation of the atomic, one-time first-Admin grant.</summary>
public sealed class SqlFirstAdminBootstrapStore : IFirstAdminBootstrapStore
{
    internal const string SystemActor = "system:oidc-first-admin-bootstrap";
    internal const string Reason = "One-time validated OIDC first-Admin bootstrap.";
    private const string AdminRoleCode = "Admin";
    private const int CommandTimeoutSeconds = 15;
    private readonly string _connectionString;

    /// <summary>Initializes the SQL bootstrap store.</summary>
    public SqlFirstAdminBootstrapStore(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString(AuditConnectionStrings.SecureOpsDb)
            ?? throw new InvalidOperationException("ConnectionStrings:SecureOpsDb is required for first-Admin bootstrap.");
    }

    /// <inheritdoc />
    public async Task<FirstAdminBootstrapDisposition> TryGrantAsync(
        FirstAdminBootstrapCommand command,
        CancellationToken cancellationToken)
    {
        await using SqlConnection connection = new(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        await connection.ExecuteAsync(Command("SET XACT_ABORT ON;", null, transaction, cancellationToken));

        short[] adminRoleIds = (await connection.QueryAsync<short>(Command(
            "SELECT RoleId FROM security.Roles WITH (UPDLOCK, HOLDLOCK) WHERE RoleCode = @RoleCode;",
            new { RoleCode = AdminRoleCode },
            transaction,
            cancellationToken))).ToArray();
        if (adminRoleIds.Length != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            return FirstAdminBootstrapDisposition.NotEligible;
        }

        short adminRoleId = adminRoleIds[0];
        bool adminEverProvisioned = await connection.QuerySingleAsync<bool>(Command(
            "SELECT CONVERT(bit, CASE WHEN EXISTS (SELECT 1 FROM security.RoleAssignments WITH (HOLDLOCK) WHERE RoleId = @RoleId) THEN 1 ELSE 0 END);",
            new { RoleId = adminRoleId },
            transaction,
            cancellationToken));
        if (adminEverProvisioned)
        {
            await transaction.RollbackAsync(cancellationToken);
            return FirstAdminBootstrapDisposition.AlreadyProvisioned;
        }

        const string eligibilitySql = """
            SELECT COUNT_BIG(1)
            FROM security.Users u WITH (UPDLOCK, HOLDLOCK)
            JOIN security.AccessRequests ar WITH (UPDLOCK, HOLDLOCK) ON ar.UserId = u.UserId
            WHERE u.UserId = @UserId
              AND u.CorporateIdentity = @StableIdentity
              AND u.AuthenticationSource = N'oidc'
              AND u.AccessStatus = N'Pending'
              AND ar.AccessRequestId = @AccessRequestId
              AND ar.Status = N'Pending'
              AND ar.Version = @AccessRequestVersion;
            """;
        long eligibleRows = await connection.QuerySingleAsync<long>(Command(
            eligibilitySql,
            command,
            transaction,
            cancellationToken));
        if (eligibleRows != 1)
        {
            await transaction.RollbackAsync(cancellationToken);
            return FirstAdminBootstrapDisposition.NotEligible;
        }

        DateTimeOffset occurredAt = DateTimeOffset.UtcNow;
        const string mutationSql = """
            UPDATE security.AccessRequests
            SET Status = N'Approved', DecidedAt = @OccurredAt,
                DecidedByCorporateIdentity = @Actor, DecisionReason = @Reason,
                Version = Version + 1
            WHERE AccessRequestId = @AccessRequestId
              AND Status = N'Pending'
              AND Version = @AccessRequestVersion;

            UPDATE security.Users
            SET AccessStatus = N'Approved', DisabledAt = NULL,
                DisabledByCorporateIdentity = NULL, AccessVersion = AccessVersion + 1
            WHERE UserId = @UserId
              AND CorporateIdentity = @StableIdentity
              AND AuthenticationSource = N'oidc'
              AND AccessStatus = N'Pending';

            INSERT INTO security.RoleAssignments
                (RoleAssignmentId, UserId, RoleId, GrantedAt, GrantedByCorporateIdentity)
            VALUES
                (NEWID(), @UserId, @RoleId, @OccurredAt, @Actor);
            """;
        await connection.ExecuteAsync(Command(
            mutationSql,
            new
            {
                command.UserId,
                command.AccessRequestId,
                command.AccessRequestVersion,
                command.StableIdentity,
                RoleId = adminRoleId,
                OccurredAt = occurredAt,
                Actor = SystemActor,
                Reason
            },
            transaction,
            cancellationToken));
        string detailsJson = JsonSerializer.Serialize(new
        {
            targetUserId = command.UserId,
            accessRequestId = command.AccessRequestId,
            role = AdminRoleCode,
            bootstrapMechanism = "ValidatedOidcFirstAdmin"
        }, AuditJson.SerializerOptions);
        const string auditSql = """
            INSERT INTO audit.AuditLog
                (OccurredAt, Actor, Action, CorrelationId, DetailsJson, SourceIp)
            VALUES
                (@OccurredAt, @Actor, @BootstrapAction, @CorrelationId, @DetailsJson, @SourceIp),
                (@OccurredAt, @Actor, @ApprovalAction, @CorrelationId, @DetailsJson, @SourceIp),
                (@OccurredAt, @Actor, @RoleAction, @CorrelationId, @DetailsJson, @SourceIp);
            """;
        await connection.ExecuteAsync(Command(
            auditSql,
            new
            {
                OccurredAt = occurredAt,
                Actor = SystemActor,
                BootstrapAction = AuditActions.FirstAdminBootstrapped,
                ApprovalAction = AuditActions.AccessApproved,
                RoleAction = AuditActions.RoleAssigned,
                command.CorrelationId,
                DetailsJson = detailsJson,
                command.SourceIp
            },
            transaction,
            cancellationToken));

        await transaction.CommitAsync(cancellationToken);
        return FirstAdminBootstrapDisposition.Applied;
    }

    private static CommandDefinition Command(
        string sql,
        object? parameters,
        IDbTransaction transaction,
        CancellationToken cancellationToken) =>
        new(sql, parameters, transaction, CommandTimeoutSeconds, cancellationToken: cancellationToken);
}
