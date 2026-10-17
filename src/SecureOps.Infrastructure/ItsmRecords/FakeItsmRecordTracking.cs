namespace SecureOps.Infrastructure.ItsmRecords;

/// <summary>
/// Deterministic synthetic provider for Development, Demo and Test (ADR-0004, ADR-0032). It makes no network call.
/// Source id <c>900001</c> (the Fake OR of <see cref="FakeItsmRecordLookup"/>) is Found with two activities and
/// two comments and synthetic status and stage labels; <c>900002</c> Ambiguous; <c>900003</c> Unknown/Unavailable;
/// <c>900004</c> Found with the activity part Unknown/IncompleteResponse and zero comments; any other valid id
/// NotFound; an invalid id Unknown/InvalidIdentifier. Results report Provider "Fake" and are never real evidence.
/// For <see cref="IItsmWasasTracking"/>: <c>900001</c> Pending/Open/AssignedToWasas, <c>900101</c>
/// Completed/Open/NotAssignedToWasas, <c>900102</c> NotApproved/Open/NotAssignedToWasas, <c>900103</c>
/// Completed/Closed with an unknown assignment (empty group); every other id, including a valid one the source does
/// not know, is Unknown (not found is never "no activity"); an invalid id is Unknown/InvalidIdentifier.
/// </summary>
public sealed class FakeItsmRecordTracking : IItsmRecordTracking, IItsmWasasTracking
{
    /// <summary>Provider name reported in results.</summary>
    public const string ProviderName = "Fake";

    /// <inheritdoc />
    public Task<ItsmWasasClassification> ClassifyAsync(string? sourceId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ItsmWasasClassification result = ItsmSourceId.Normalize(sourceId) switch
        {
            null => ItsmWasasClassification.Unknown(ItsmRecordLookupReason.InvalidIdentifier, ProviderName),
            "900001" => new(ItsmWasasActivityClass.Pending, ItsmOrClosureClass.Open, ItsmAssignmentClass.AssignedToWasas, ItsmRecordLookupReason.None, ProviderName),
            "900101" => new(ItsmWasasActivityClass.Completed, ItsmOrClosureClass.Open, ItsmAssignmentClass.NotAssignedToWasas, ItsmRecordLookupReason.None, ProviderName),
            "900102" => new(ItsmWasasActivityClass.NotApproved, ItsmOrClosureClass.Open, ItsmAssignmentClass.NotAssignedToWasas, ItsmRecordLookupReason.None, ProviderName),
            "900103" => new(ItsmWasasActivityClass.Completed, ItsmOrClosureClass.Closed, ItsmAssignmentClass.Unknown, ItsmRecordLookupReason.None, ProviderName),
            "900003" => ItsmWasasClassification.Unknown(ItsmRecordLookupReason.Unavailable, ProviderName),
            _ => ItsmWasasClassification.Unknown(ItsmRecordLookupReason.IncompleteResponse, ProviderName)
        };
        return Task.FromResult(result);
    }

    /// <inheritdoc />
    public Task<ItsmRecordTrackingResult> GetOrTrackingAsync(string? sourceId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string? id = ItsmSourceId.Normalize(sourceId);
        ItsmRecordTrackingResult result = id switch
        {
            null => ItsmRecordTrackingResult.Unknown(null, ItsmRecordLookupReason.InvalidIdentifier, ProviderName),
            "900001" => ItsmRecordTrackingResult.Found(id,
                new ItsmOrState("2026-01-05T02:30:00", "9301", "9401", "Synthetic status", "Synthetic stage"),
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
/// <c>synthetic.user</c> (exact case, like the source) is Matched, <c>synthetic.dup</c> Ambiguous,
/// <c>synthetic.down</c> Unknown/Unavailable, any other valid name NotMatched, an invalid name Unknown/InvalidIdentifier. Registration number <c>990001</c>
/// (also written <c>0990001</c>) is the same user as <c>synthetic.user</c>; <c>990004</c> is another user, so it
/// conflicts with <c>synthetic.user</c> in the combined match; <c>990002</c> is Ambiguous, <c>990003</c>
/// Unknown/Unavailable, any other valid number NotMatched. Results report Provider "Fake" and are never real evidence.
/// </summary>
public sealed class FakeItsmUserMapping : IItsmUserMapping
{
    /// <summary>Provider name reported in results.</summary>
    public const string ProviderName = "Fake";

    private static readonly ItsmUserReference _syntheticUser = new("920001", "9601", "9701");
    private static readonly ItsmUserReference _otherSyntheticUser = new("920004", "9601", "9702");

    /// <inheritdoc />
    public Task<ItsmUserMatchResult> MatchByUserNameAsync(string? userName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string? name = ItsmUserName.Normalize(userName);
        ItsmUserMatchResult result = name switch
        {
            null => ItsmUserMatchResult.Unknown(ItsmRecordLookupReason.InvalidIdentifier, ProviderName),
            "synthetic.user" => ItsmUserMatchResult.Matched(_syntheticUser, ProviderName),
            "synthetic.dup" => ItsmUserMatchResult.Ambiguous(ProviderName),
            "synthetic.down" => ItsmUserMatchResult.Unknown(ItsmRecordLookupReason.Unavailable, ProviderName),
            _ => ItsmUserMatchResult.NotMatched(ProviderName)
        };
        return Task.FromResult(result);
    }

    /// <inheritdoc />
    public Task<ItsmUserMatchResult> MatchByRegistrationNumberAsync(string? registrationNumber, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ItsmUserMatchResult result = ItsmRegistrationNumber.Normalize(registrationNumber) switch
        {
            null => ItsmUserMatchResult.Unknown(ItsmRecordLookupReason.InvalidIdentifier, ProviderName),
            "990001" => ItsmUserMatchResult.Matched(_syntheticUser, ProviderName),
            "990002" => ItsmUserMatchResult.Ambiguous(ProviderName),
            "990003" => ItsmUserMatchResult.Unknown(ItsmRecordLookupReason.Unavailable, ProviderName),
            "990004" => ItsmUserMatchResult.Matched(_otherSyntheticUser, ProviderName),
            _ => ItsmUserMatchResult.NotMatched(ProviderName)
        };
        return Task.FromResult(result);
    }

    /// <inheritdoc />
    public async Task<ItsmUserMatchResult> MatchAsync(string? registrationNumber, string? userName, CancellationToken cancellationToken) =>
        (await ItsmUserMatchRules.MatchAsync(this, registrationNumber, userName, ProviderName, cancellationToken)).Result;
}
