namespace SecureOps.Infrastructure.Commands;

/// <summary>Durable command idempotency and bounded execution-lease boundary.</summary>
public interface ICommandIdempotencyStore
{
    /// <summary>Atomically starts or resolves a command execution.</summary>
    public Task<CommandBeginResult> TryBeginAsync(string commandName, string targetId, string idempotencyKey, string actor, TimeSpan leaseDuration, CancellationToken cancellationToken);
    /// <summary>Marks a command successful.</summary>
    public Task CompleteAsync(string commandName, string targetId, string idempotencyKey, Guid executionToken, CancellationToken cancellationToken);
    /// <summary>Persists a safe command failure code.</summary>
    public Task FailAsync(string commandName, string targetId, string idempotencyKey, Guid executionToken, string errorCode, CancellationToken cancellationToken);
}

/// <summary>Command begin disposition.</summary>
public enum CommandBeginDisposition
{
    /// <summary>The caller acquired the execution lease.</summary>
    Acquired,
    /// <summary>The same command is still executing.</summary>
    InProgress,
    /// <summary>The same command previously completed.</summary>
    Completed,
    /// <summary>The same command previously failed.</summary>
    Failed,
    /// <summary>The key belongs to another actor.</summary>
    ActorConflict
}

/// <summary>Result of atomic command acquisition.</summary>
public sealed record CommandBeginResult(CommandBeginDisposition Disposition, string? ErrorCode, Guid? ExecutionToken = null);
