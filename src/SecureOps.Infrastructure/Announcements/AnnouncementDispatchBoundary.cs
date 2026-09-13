using SecureOps.Domain.Announcements;

namespace SecureOps.Infrastructure.Announcements;

/// <summary>Per-recipient transport evidence, not a mailbox-delivery assertion.</summary>
public sealed record AnnouncementTransportOutcome(string State, IReadOnlyList<string> Accepted, IReadOnlyList<string> Rejected);
/// <summary>Worker-owned durable authorization/write-gate/claim boundary; no implementation registered yet.</summary>
public interface IAnnouncementDispatchClaim
{
    /// <summary>Recheck persisted authorization, explicit confirmed intent, fingerprint and write gates; atomically deny duplicates.</summary>
    public Task<bool> ClaimAsync(Guid preparationId, string fingerprint, CancellationToken token);
}
/// <summary>Consumes exact snapshot bytes. Message-ID alone is not deduplication.</summary>
public interface IAnnouncementTransport
{
    /// <summary>Never retry uncertain or partially accepted submissions inside the adapter.</summary>
    public Task<AnnouncementTransportOutcome> SubmitAsync(PreparedAnnouncement snapshot, CancellationToken token);
}
/// <summary>Unregistered boundary for later Hangfire composition; local tests inject capture only.</summary>
public sealed class AnnouncementDispatchBoundary(IAnnouncementDispatchClaim claim, IAnnouncementTransport transport)
{
    /// <summary>No queue, automatic retry or production transport. Durable outcome recording belongs to the future Worker contract.</summary>
    public async Task<AnnouncementTransportOutcome> ExecuteAsync(PreparedAnnouncement snapshot, CancellationToken token)
    {
        if (snapshot.Fingerprint != AnnouncementService.PreparationFingerprint(snapshot)
            || !await claim.ClaimAsync(snapshot.Id, snapshot.Fingerprint, token))
        { return new("NotDispatched", [], []); }
        try
        { return await transport.SubmitAsync(snapshot, token); }
        catch (Exception ex) when (ex is IOException or OperationCanceledException) { return new("UnknownOutcome", [], []); }
    }
}
