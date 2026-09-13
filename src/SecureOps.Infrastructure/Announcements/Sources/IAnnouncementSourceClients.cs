using SecureOps.Domain.Announcements;

namespace SecureOps.Infrastructure.Announcements.Sources;

/// <summary>Bounded collection read. Complete=false means the ceiling or a page limit truncated it.</summary>
public sealed record CollectionMembershipResult(IReadOnlyList<SourceDevice> Devices, bool Complete,
    int PagesRead, string[] Warnings);

/// <summary>
/// Device-to-service relationship outcome. Resolution is Resolved, Ambiguous, Missing or Failed;
/// every candidate is returned so no caller can select a positional first result.
/// </summary>
public sealed record ServiceLookupResult(string Device, string[] Candidates, string Resolution);

/// <summary>Proposed OCO window, or an explicit Missing/Ambiguous/Failed resolution.</summary>
public sealed record ChangeWindowResult(string? StartText, string? FinishText, string Resolution);

/// <summary>Read-only SCCM collection membership. Membership never implies OCO scope or approval.</summary>
public interface ICollectionMembershipClient
{
    /// <summary>Reads bounded collection membership, paging explicitly rather than assuming one page.</summary>
    public Task<CollectionMembershipResult> GetDevicesAsync(string collectionId, CancellationToken cancellationToken);
}

/// <summary>Read-only service-relationship and operational-change source.</summary>
public interface IAnnouncementServiceSourceClient
{
    /// <summary>Resolves the services related to exactly one device name.</summary>
    public Task<ServiceLookupResult> GetDeviceServicesAsync(string device, CancellationToken cancellationToken);

    /// <summary>Reads the proposed work window for exactly one OCO reference.</summary>
    public Task<ChangeWindowResult> GetChangeWindowAsync(string ocoReference, CancellationToken cancellationToken);
}

/// <summary>Fails closed for both source roles when configuration selects no provider.</summary>
public sealed class DisabledAnnouncementSourceClient : ICollectionMembershipClient, IAnnouncementServiceSourceClient
{
    /// <inheritdoc />
    public Task<CollectionMembershipResult> GetDevicesAsync(string collectionId, CancellationToken cancellationToken) => throw Disabled();
    /// <inheritdoc />
    public Task<ServiceLookupResult> GetDeviceServicesAsync(string device, CancellationToken cancellationToken) => throw Disabled();
    /// <inheritdoc />
    public Task<ChangeWindowResult> GetChangeWindowAsync(string ocoReference, CancellationToken cancellationToken) => throw Disabled();
    private static AnnouncementSourceException Disabled() => new("AnnouncementSourceDisabled", false);
}

/// <summary>Sanitized source failure. Never carries a remote body, credential or session value.</summary>
public sealed class AnnouncementSourceException(string errorCode, bool retryable)
    : Exception("Announcement source operation failed.")
{
    /// <summary>Stable contract error code surfaced to the reviewer.</summary>
    public string ErrorCode { get; } = errorCode;
    /// <summary>Whether a later identical read could plausibly succeed.</summary>
    public bool Retryable { get; } = retryable;
}
