namespace SecureOps.Infrastructure.ItsmRecords;

/// <summary>Default provider: every tracking read is Unknown/NotConfigured. It never answers Found or NotFound.</summary>
public sealed class DisabledItsmRecordTracking : IItsmRecordTracking
{
    /// <summary>Provider name reported in results.</summary>
    public const string ProviderName = "Disabled";

    /// <inheritdoc />
    public Task<ItsmRecordTrackingResult> GetOrTrackingAsync(string? sourceId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ItsmRecordTrackingResult.Unknown(
            ItsmSourceId.Normalize(sourceId), ItsmRecordLookupReason.NotConfigured, ProviderName));
    }
}

/// <summary>Default provider: every user mapping is Unknown/NotConfigured. It never answers Matched or NotMatched.</summary>
public sealed class DisabledItsmUserMapping : IItsmUserMapping
{
    /// <summary>Provider name reported in results.</summary>
    public const string ProviderName = "Disabled";

    /// <inheritdoc />
    public Task<ItsmUserMatchResult> MatchByUserNameAsync(string? userName, CancellationToken cancellationToken) =>
        NotConfigured(cancellationToken);

    /// <inheritdoc />
    public Task<ItsmUserMatchResult> MatchByRegistrationNumberAsync(string? registrationNumber, CancellationToken cancellationToken) =>
        NotConfigured(cancellationToken);

    /// <inheritdoc />
    public Task<ItsmUserMatchResult> MatchAsync(string? registrationNumber, string? userName, CancellationToken cancellationToken) =>
        NotConfigured(cancellationToken);

    private static Task<ItsmUserMatchResult> NotConfigured(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ItsmUserMatchResult.Unknown(ItsmRecordLookupReason.NotConfigured, ProviderName));
    }
}
