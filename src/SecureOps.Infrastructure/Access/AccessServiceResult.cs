namespace SecureOps.Infrastructure.Access;

/// <summary>Safe result from application access orchestration.</summary>
public sealed record AccessServiceResult<T>(T? Value, string? ErrorCode)
{
    /// <summary>Whether the operation succeeded.</summary>
    public bool IsSuccess => ErrorCode is null;

    /// <summary>Creates a successful result.</summary>
    public static AccessServiceResult<T> Success(T value) => new(value, null);

    /// <summary>Creates a failed result.</summary>
    public static AccessServiceResult<T> Fail(string code) => new(default, code);
}
