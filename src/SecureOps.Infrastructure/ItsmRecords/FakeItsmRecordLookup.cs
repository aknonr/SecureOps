namespace SecureOps.Infrastructure.ItsmRecords;

/// <summary>
/// Deterministic synthetic provider for Development, Demo and Test (ADR-0004). It makes no network call and
/// covers all four outcomes with placeholder numbers, so a consumer can exercise every state:
/// <c>-000001</c> Found, <c>-000002</c> Ambiguous, <c>-000003</c> Unknown/Unavailable, any other valid number
/// NotFound, an invalid number Unknown/InvalidNumber. Results report Provider "Fake" and are never real evidence.
/// </summary>
public sealed class FakeItsmRecordLookup : IItsmRecordLookup
{
    /// <summary>Provider name reported in results.</summary>
    public const string ProviderName = "Fake";

    private static readonly Dictionary<string, ItsmRecord> _records = new(StringComparer.Ordinal)
    {
        ["OR-000001"] = new("900001", "OR-000001", "Synthetic OR record", null, null, "9001", null, null),
        ["OCO-000001"] = new("900101", "OCO-000001", "Synthetic OCO record", "9101", "9201", "9001",
            "2026-01-05T01:00:00", "2026-01-05T03:00:00")
    };

    /// <inheritdoc />
    public Task<ItsmRecordLookupResult> LookupAsync(ItsmRecordKind kind, string? number, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string? normalized = ItsmRecordNumber.Normalize(kind, number);
        if (normalized is null)
        {
            return Task.FromResult(ItsmRecordLookupResult.Unknown(kind, null, ItsmRecordLookupReason.InvalidNumber, ProviderName));
        }

        string digits = normalized[ItsmRecordNumber.Prefix(kind).Length..];
        ItsmRecordLookupResult result = digits switch
        {
            "000002" => ItsmRecordLookupResult.Ambiguous(kind, normalized, ProviderName),
            "000003" => ItsmRecordLookupResult.Unknown(kind, normalized, ItsmRecordLookupReason.Unavailable, ProviderName),
            _ when _records.TryGetValue(normalized, out ItsmRecord? record) =>
                ItsmRecordLookupResult.Found(kind, normalized, record, ProviderName),
            _ => ItsmRecordLookupResult.NotFound(kind, normalized, ProviderName)
        };
        return Task.FromResult(result);
    }
}
