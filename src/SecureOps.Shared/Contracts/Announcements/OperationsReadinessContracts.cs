namespace SecureOps.Shared.Contracts.Announcements;

/// <summary>Safe stage status; configured never means corporate connectivity was verified.</summary>
public sealed record AnnouncementSourceReadiness(string State, string Stage, string[] Missing,
    string WorkerState, int MatchingWorkers, DateTimeOffset? LastHeartbeat, DateTimeOffset CheckedAt);

/// <summary>One allowlisted effective option and the provider that supplied it.</summary>
public sealed record EffectiveOperationSetting(string Key, string Value, string Provider);

/// <summary>One configured local asset; no directory enumeration or public-image dependency.</summary>
public sealed record OperationAssetReadiness(string Revision, string State, string? Format, string? Sha256);

/// <summary>Authorized administrative details, never a general configuration dump.</summary>
public sealed record OperationsReadiness(string RuntimeIdentity, int ProcessId, DateTimeOffset CapturedAt,
    string ConfigurationFingerprint, IReadOnlyList<EffectiveOperationSetting> Settings,
    AnnouncementSourceReadiness Source, IReadOnlyList<OperationAssetReadiness> Assets,
    string AssetDirectory, string ReportDirectory, string ReportState)
{
    /// <summary>Configured six-role bundle mapping status, distinct from individual file decoding.</summary>
    public IReadOnlyList<AnnouncementBanner> Bundles { get; init; } = [];
}
