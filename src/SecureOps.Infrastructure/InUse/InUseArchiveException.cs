namespace SecureOps.Infrastructure.InUse;

/// <summary>Stable archive failure category, without exposing physical paths to operators.</summary>
public sealed class InUseArchiveException(string code, Exception inner) : Exception(code, inner)
{
    /// <summary>Safe problem code; inner details remain private.</summary>
    public string Code { get; } = code;
}
