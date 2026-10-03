using System.Collections.Frozen;
using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using SecureOps.Infrastructure.Audit;
using SecureOps.Infrastructure.OperationalRecords;

namespace SecureOps.Infrastructure.Reporting;

/// <summary>Audit evidence held by the in-memory audit writer.</summary>
public sealed class InMemoryReportingAuditFacts(InMemoryAuditWriter writer) : IReportingAuditFacts
{
    private static readonly FrozenSet<string> _actions = ReportingMetricCatalog.FactActions.ToFrozenSet(StringComparer.Ordinal);

    /// <inheritdoc />
    public ReportingSourceKind Kind => ReportingSourceKind.InMemory;

    /// <inheritdoc />
    public Task<IReadOnlyList<ReportingAuditFact>> ReadAsync(DateTimeOffset toExclusive, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Serialize details exactly as SqlAuditWriter stores DetailsJson, so JSON paths read identically.
        IReadOnlyList<ReportingAuditFact> facts =
        [
            .. writer.Events
                .Where(entry => entry.OccurredAt < toExclusive && _actions.Contains(entry.Action))
                .Select(entry => new ReportingAuditFact(
                    entry.OccurredAt,
                    entry.Actor,
                    entry.Action,
                    entry.CorrelationId,
                    entry.Details is null ? null : JsonSerializer.Serialize(entry.Details, AuditJson.SerializerOptions)))
        ];
        return Task.FromResult(facts);
    }
}

/// <summary>Workflow evidence held by the in-memory Operational Record repository.</summary>
public sealed class InMemoryReportingWorkflowFacts(InMemoryOperationalRecordRepository records) : IReportingWorkflowFacts
{
    /// <inheritdoc />
    public ReportingSourceKind Kind => ReportingSourceKind.InMemory;

    /// <inheritdoc />
    public Task<IReadOnlyList<ReportingWorkflowFact>> ReadHistoryAsync(DateTimeOffset toExclusive, CancellationToken cancellationToken) =>
        records.ReadWorkflowHistoryAsync(toExclusive, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<ReportingTransferFact>> ReadTransfersAsync(CancellationToken cancellationToken) =>
        records.ReadTransfersAsync(cancellationToken);
}

/// <summary>Audit evidence read row-by-row from the SQL reporting read model.</summary>
public sealed class SqlReportingAuditFacts(IConfiguration configuration) : IReportingAuditFacts
{
    private const int _commandTimeoutSeconds = 30;

    private readonly string _connectionString = configuration.GetConnectionString(AuditConnectionStrings.SecureOpsDb)
        ?? throw new InvalidOperationException("ConnectionStrings:SecureOpsDb is required for SQL audit reporting.");

    /// <inheritdoc />
    public ReportingSourceKind Kind => ReportingSourceKind.SqlServer;

    /// <inheritdoc />
    public async Task<IReadOnlyList<ReportingAuditFact>> ReadAsync(DateTimeOffset toExclusive, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT OccurredAt, Actor, Action, CorrelationId, DetailsJson
            FROM reporting.ManagementAuditEvents
            WHERE OccurredAt < @ToExclusive AND Action IN @Actions;
            """;
        await using SqlConnection connection = new(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return [.. await connection.QueryAsync<ReportingAuditFact>(new CommandDefinition(
            sql,
            new { ToExclusive = toExclusive, Actions = ReportingMetricCatalog.FactActions },
            commandTimeout: _commandTimeoutSeconds,
            cancellationToken: cancellationToken))];
    }
}

/// <summary>Workflow evidence read row-by-row from the SQL reporting read model.</summary>
public sealed class SqlReportingWorkflowFacts(IConfiguration configuration) : IReportingWorkflowFacts
{
    private const int _commandTimeoutSeconds = 30;

    private readonly string _connectionString = configuration.GetConnectionString(AuditConnectionStrings.SecureOpsDb)
        ?? throw new InvalidOperationException("ConnectionStrings:SecureOpsDb is required for SQL workflow reporting.");

    /// <inheritdoc />
    public ReportingSourceKind Kind => ReportingSourceKind.SqlServer;

    /// <inheritdoc />
    public async Task<IReadOnlyList<ReportingWorkflowFact>> ReadHistoryAsync(DateTimeOffset toExclusive, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT OperationalRecordId, WorkflowState, OccurredAt
            FROM reporting.ManagementWorkflowEvents
            WHERE OccurredAt < @ToExclusive;
            """;
        await using SqlConnection connection = new(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return [.. await connection.QueryAsync<ReportingWorkflowFact>(new CommandDefinition(
            sql, new { ToExclusive = toExclusive }, commandTimeout: _commandTimeoutSeconds, cancellationToken: cancellationToken))];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ReportingTransferFact>> ReadTransfersAsync(CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT OperationalRecordId, ReconciliationRequired, TransferUpdatedAt AS UpdatedAt
            FROM reporting.ManagementOperationalStatus;
            """;
        await using SqlConnection connection = new(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return [.. await connection.QueryAsync<ReportingTransferFact>(new CommandDefinition(
            sql, commandTimeout: _commandTimeoutSeconds, cancellationToken: cancellationToken))];
    }
}
