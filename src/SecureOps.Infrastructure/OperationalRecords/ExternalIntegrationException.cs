namespace SecureOps.Infrastructure.OperationalRecords;

/// <summary>Safe integration exception carrying no remote payload or credential data.</summary>
public sealed class ExternalIntegrationException : Exception
{
    /// <summary>Initializes a safe integration failure.</summary>
    public ExternalIntegrationException(string errorCode, bool retryable, bool outcomeUnknown = false, Exception? innerException = null)
        : base(errorCode, innerException)
    {
        ErrorCode = errorCode;
        Retryable = retryable;
        OutcomeUnknown = outcomeUnknown;
    }

    /// <summary>Stable safe error code.</summary>
    public string ErrorCode { get; }
    /// <summary>Whether a later retry can be considered.</summary>
    public bool Retryable { get; }
    /// <summary>Whether a remote write may have succeeded despite the failure.</summary>
    public bool OutcomeUnknown { get; }
}
