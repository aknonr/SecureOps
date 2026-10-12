namespace SecureOps.Infrastructure.ItsmRecords;

/// <summary>
/// Deterministic synthetic provider for Development, Demo and Test (ADR-0004, ADR-0032). It makes no network call.
/// Source id <c>900001</c> (the Fake OR of <see cref="FakeItsmRecordLookup"/>) is Found with two activities and
/// two comments; <c>900002</c> Ambiguous; <c>900003</c> Unknown/Unavailable; <c>900004</c> Found with the activity
/// part Unknown/IncompleteResponse and zero comments; any other valid id NotFound; an invalid id
/// Unknown/InvalidIdentifier. Results report Provider "Fake" and are never real evidence.
/// </summary>
public sealed class FakeItsmRecordTracking : IItsmRecordTracking
{
    /// <summary>Provider name reported in results.</summary>
    public const string ProviderName = "Fake";

    /// <inheritdoc />
    public Task<ItsmRecordTrackingResult> GetOrTrackingAsync(string? sourceId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string? id = ItsmSourceId.Normalize(sourceId);
        ItsmRecordTrackingResult result = id switch
        {
            null => ItsmRecordTrackingResult.Unknown(null, ItsmRecordLookupReason.InvalidIdentifier, ProviderName),
            "900001" => ItsmRecordTrackingResult.Found(id,
                new ItsmOrState("2026-01-05T02:30:00", "9301", "9401"),
                ItsmTrackingPart<IReadOnlyList<ItsmActivity>>.Known(
                [
                    new ItsmActivity("910001", "9001", "9501", "2026-01-05T01:00:00", "2026-01-05T01:45:00"),
                    new ItsmActivity("910002", "9002", "9502", "2026-01-05T01:45:00", null)
                ]),
                ItsmTrackingPart<ItsmCommentSummary>.Known(new ItsmCommentSummary(2, "2026-01-05T02:30:00")),
                ProviderName),
            "900002" => ItsmRecordTrackingResult.Ambiguous(id, ProviderName),
            "900003" => ItsmRecordTrackingResult.Unknown(id, ItsmRecordLookupReason.Unavailable, ProviderName),
            "900004" => ItsmRecordTrackingResult.Found(id,
                new ItsmOrState(null, null, null),
                ItsmTrackingPart<IReadOnlyList<ItsmActivity>>.Unknown(ItsmRecordLookupReason.IncompleteResponse),
                ItsmTrackingPart<ItsmCommentSummary>.Known(new ItsmCommentSummary(0, null)),
                ProviderName),
            _ => ItsmRecordTrackingResult.NotFound(id, ProviderName)
        };
        return Task.FromResult(result);
    }
}

/// <summary>
/// Deterministic synthetic provider for Development, Demo and Test (ADR-0004, ADR-0032). User name
/// <c>synthetic.user</c> is Matched, <c>synthetic.dup</c> Ambiguous, <c>synthetic.down</c> Unknown/Unavailable,
/// any other valid name NotMatched, an invalid name Unknown/InvalidIdentifier. Results report Provider "Fake".
/// </summary>
public sealed class FakeItsmUserMapping : IItsmUserMapping
{
    /// <summary>Provider name reported in results.</summary>
    public const string ProviderName = "Fake";

    /// <inheritdoc />
    public Task<ItsmUserMatchResult> MatchByUserNameAsync(string? userName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string? name = ItsmUserName.Normalize(userName);
        ItsmUserMatchResult result = name?.ToLowerInvariant() switch
        {
            null => ItsmUserMatchResult.Unknown(ItsmRecordLookupReason.InvalidIdentifier, ProviderName),
            "synthetic.user" => ItsmUserMatchResult.Matched(new ItsmUserReference("920001", "9601", "9701"), ProviderName),
            "synthetic.dup" => ItsmUserMatchResult.Ambiguous(ProviderName),
            "synthetic.down" => ItsmUserMatchResult.Unknown(ItsmRecordLookupReason.Unavailable, ProviderName),
            _ => ItsmUserMatchResult.NotMatched(ProviderName)
        };
        return Task.FromResult(result);
    }
}
