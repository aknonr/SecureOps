using SecureOps.Shared.Contracts.OperationalRecords;

namespace SecureOps.Infrastructure.OperationalRecords;

/// <summary>Explicit post-state contract; corporate selectors/transition/closer are not inferred from active-list absence.</summary>
public interface ISourceClosureVerifier
{
    /// <summary>Whether this provider has an established exact-record closed-state contract.</summary>
    public bool CanVerify { get; }
    /// <summary>Read-only verification of an acknowledged attempt, never an update or a retry.</summary>
    public Task<SourceClosureObservation> VerifyAsync(string sourceId, string code, string jiraKey, string attempt, CancellationToken token);
}

/// <summary>Only the existing synthetic providers have a complete final-state read contract today.</summary>
public sealed class SourceClosureVerifier(IOperationalRecordClient source, string provider) : ISourceClosureVerifier
{
    /// <inheritdoc />
    public bool CanVerify => provider is "Fake" or "Simulation";
    /// <inheritdoc />
    public async Task<SourceClosureObservation> VerifyAsync(string sourceId, string code, string jiraKey, string attempt, CancellationToken token)
    {
        if (!CanVerify)
        { return new("Unavailable", sourceId, code, jiraKey, attempt, DateTimeOffset.UtcNow, null, null, "Unresolved", "SourceCloseVerificationUnavailable"); }
        OperationalRecordSourceItem? current = await source.GetByIdAsync(sourceId, token);
        bool exact = current?.SourceRecordId == sourceId && current.OrCode == code;
        return new(exact && !current!.IsOpen ? "VerifiedClosed" : "Uncertain", sourceId, code, jiraKey, attempt, DateTimeOffset.UtcNow,
            exact ? OperationalRecordSourceConcurrency.Create(current!) : null, null, "SyntheticExactState-v1", exact && !current!.IsOpen ? null : "SourceCloseUnverified");
    }
}
