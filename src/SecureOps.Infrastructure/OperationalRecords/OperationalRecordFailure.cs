namespace SecureOps.Infrastructure.OperationalRecords;

/// <summary>Safe expected workflow failure.</summary>
public sealed record OperationalRecordFailure(string Code, string Stage, bool Retryable);

/// <summary>Value-or-failure result used by operational-record services.</summary>
public sealed record OperationalRecordResult<T>(T? Value, OperationalRecordFailure? Failure)
{
    /// <summary>Whether the operation succeeded.</summary>
    public bool IsSuccess => Failure is null;

    /// <summary>Creates a successful result.</summary>
    public static OperationalRecordResult<T> Success(T value) => new(value, null);

    /// <summary>Creates a failed result.</summary>
    public static OperationalRecordResult<T> Fail(string code, string stage, bool retryable) =>
        new(default, new OperationalRecordFailure(code, stage, retryable));
}
