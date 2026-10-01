namespace SecureOps.Shared.Configuration;

/// <summary>Reusable durable command-execution lease settings.</summary>
public sealed class CommandIdempotencyOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "CommandIdempotency";

    /// <summary>Maximum time an interrupted command remains in progress before safe reacquisition is considered.</summary>
    public int ExecutionLeaseSeconds { get; set; } = 120;

    /// <summary>Maximum accepted caller-supplied idempotency key length.</summary>
    public int MaxKeyLength { get; set; } = 128;
}
