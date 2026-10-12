namespace SecureOps.Infrastructure.ItsmRecords;

/// <summary>Default provider: every lookup is Unknown/NotConfigured. It never answers Found or NotFound.</summary>
public sealed class DisabledItsmRecordLookup : IItsmRecordLookup
{
    /// <summary>Provider name reported in results.</summary>
    public const string ProviderName = "Disabled";

    /// <inheritdoc />
    public Task<ItsmRecordLookupResult> LookupAsync(ItsmRecordKind kind, string? number, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ItsmRecordLookupResult.Unknown(
            kind, ItsmRecordNumber.Normalize(kind, number), ItsmRecordLookupReason.NotConfigured, ProviderName));
    }
}
