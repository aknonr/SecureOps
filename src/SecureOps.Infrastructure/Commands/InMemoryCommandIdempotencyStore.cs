namespace SecureOps.Infrastructure.Commands;

/// <summary>Concurrency-safe local command store; SQL is required for restart durability.</summary>
public sealed class InMemoryCommandIdempotencyStore : ICommandIdempotencyStore
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, Execution> _executions = new(StringComparer.Ordinal);
    private readonly TimeProvider _timeProvider;

    /// <summary>Initializes the store.</summary>
    public InMemoryCommandIdempotencyStore(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public async Task<CommandBeginResult> TryBeginAsync(string commandName, string targetId, string idempotencyKey, string actor, TimeSpan leaseDuration, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            string key = Composite(commandName, targetId, idempotencyKey);
            DateTimeOffset now = _timeProvider.GetUtcNow();
            if (!_executions.TryGetValue(key, out Execution? execution))
            {
                var token = Guid.NewGuid();
                _executions[key] = new Execution(actor, CommandState.InProgress, now.Add(leaseDuration), token, null);
                return new CommandBeginResult(CommandBeginDisposition.Acquired, null, token);
            }

            if (!string.Equals(execution.Actor, actor, StringComparison.OrdinalIgnoreCase) && execution.LeaseExpiresAt > now)
            {
                return new CommandBeginResult(CommandBeginDisposition.ActorConflict, null);
            }

            if (execution.State == CommandState.Completed)
            {
                return string.Equals(execution.Actor, actor, StringComparison.OrdinalIgnoreCase)
                    ? new CommandBeginResult(CommandBeginDisposition.Completed, null)
                    : new CommandBeginResult(CommandBeginDisposition.ActorConflict, null);
            }

            if (execution.State == CommandState.Failed)
            {
                return string.Equals(execution.Actor, actor, StringComparison.OrdinalIgnoreCase)
                    ? new CommandBeginResult(CommandBeginDisposition.Failed, execution.ErrorCode)
                    : new CommandBeginResult(CommandBeginDisposition.ActorConflict, null);
            }

            if (execution.LeaseExpiresAt > now)
            {
                return new CommandBeginResult(CommandBeginDisposition.InProgress, null);
            }

            var reacquiredToken = Guid.NewGuid();
            _executions[key] = new Execution(actor, CommandState.InProgress, now.Add(leaseDuration), reacquiredToken, null);
            return new CommandBeginResult(CommandBeginDisposition.Acquired, null, reacquiredToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public Task CompleteAsync(string commandName, string targetId, string idempotencyKey, Guid executionToken, CancellationToken cancellationToken) =>
        UpdateAsync(commandName, targetId, idempotencyKey, executionToken, CommandState.Completed, null, cancellationToken);

    /// <inheritdoc />
    public Task FailAsync(string commandName, string targetId, string idempotencyKey, Guid executionToken, string errorCode, CancellationToken cancellationToken) =>
        UpdateAsync(commandName, targetId, idempotencyKey, executionToken, CommandState.Failed, errorCode, cancellationToken);

    private async Task UpdateAsync(string commandName, string targetId, string idempotencyKey, Guid executionToken, CommandState state, string? errorCode, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            string key = Composite(commandName, targetId, idempotencyKey);
            if (_executions.TryGetValue(key, out Execution? execution)
                && execution.State == CommandState.InProgress
                && execution.ExecutionToken == executionToken)
            {
                _executions[key] = execution with { State = state, ErrorCode = errorCode };
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private static string Composite(string commandName, string targetId, string idempotencyKey) => $"{commandName}\n{targetId}\n{idempotencyKey}";
    private sealed record Execution(string Actor, CommandState State, DateTimeOffset LeaseExpiresAt, Guid ExecutionToken, string? ErrorCode);
    private enum CommandState { InProgress, Completed, Failed }
}
