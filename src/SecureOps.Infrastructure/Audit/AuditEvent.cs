namespace SecureOps.Infrastructure.Audit;

/// <summary>
/// Internal representation of an audit entry before persistence.
/// </summary>
public sealed class AuditEvent
{
    /// <summary>
    /// Time the audited action occurred.
    /// </summary>
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Actor identity such as CONTOSO\user or system:api.
    /// </summary>
    public required string Actor { get; init; }

    /// <summary>
    /// Canonical action code.
    /// </summary>
    public required string Action { get; init; }

    /// <summary>
    /// Optional related alert ID.
    /// </summary>
    public Guid? AlertId { get; init; }

    /// <summary>
    /// Optional related server name.
    /// </summary>
    public string? ServerName { get; init; }

    /// <summary>
    /// Optional trace correlation ID.
    /// </summary>
    public string? CorrelationId { get; init; }

    /// <summary>
    /// Optional source IP for HTTP-originated actions.
    /// </summary>
    public string? SourceIp { get; init; }

    /// <summary>
    /// Structured action-specific details.
    /// </summary>
    public object? Details { get; init; }
}
