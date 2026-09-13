using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SecureOps.Domain.Announcements;
using SecureOps.Shared.Configuration;

namespace SecureOps.Infrastructure.Announcements.Sources;

/// <summary>
/// Builds one immutable snapshot from the three independent source facts: collection selection supplies
/// devices, device relationships supply affected services, and the OCO supplies proposed work dates.
/// Each fact keeps its own retrieval timestamp and resolution; nothing is inferred across them.
/// Collection membership is not evidence of OCO scope, and no restart time is derived from any value.
/// </summary>
public sealed class AnnouncementSourceCollector(
    ICollectionMembershipClient collections,
    IAnnouncementServiceSourceClient services,
    IOptions<AnnouncementSourceOptions> options,
    TimeProvider time,
    ILogger<AnnouncementSourceCollector> logger)
{
    /// <summary>Collects evidence under the configured concurrency and wall-clock budget.</summary>
    public async Task<AnnouncementSourceSnapshot> CollectAsync(Guid jobId, Guid draftId, Guid owner,
        string profile, string collectionId, string ocoReference, CancellationToken cancellationToken)
    {
        AnnouncementSourceOptions settings = options.Value;
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(settings.JobTimeoutSeconds, 30, 3600)));
        CancellationToken token = budget.Token;
        List<string> warnings = [];

        CollectionMembershipResult membership = await collections.GetDevicesAsync(collectionId, token);
        warnings.AddRange(membership.Warnings);

        var relationships = new ServiceLookupResult[membership.Devices.Count];
        using SemaphoreSlim gate = new(Math.Clamp(settings.ServiceLookupConcurrency, 1, 16));
        await Task.WhenAll(membership.Devices.Select(async (device, index) =>
        {
            await gate.WaitAsync(token);
            try
            { relationships[index] = await LookupAsync(device.Name, settings, token); }
            finally
            { gate.Release(); }
        }));

        DateTimeOffset resolvedAt = time.GetUtcNow();
        Dictionary<string, SortedSet<string>> byService = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string> resolution = new(StringComparer.OrdinalIgnoreCase);
        int ambiguous = 0, missing = 0, failed = 0;
        foreach (ServiceLookupResult relationship in relationships)
        {
            switch (relationship.Resolution)
            {
                case "Missing":
                    missing++;
                    break;
                case "Failed":
                    failed++;
                    break;
                case "Ambiguous":
                    ambiguous++;
                    break;
            }
            foreach (string candidate in relationship.Candidates)
            {
                if (!byService.TryGetValue(candidate, out SortedSet<string>? devices))
                { byService[candidate] = devices = new(StringComparer.OrdinalIgnoreCase); }
                devices.Add(relationship.Device);
                // An ambiguous device taints the services it named; a single clean device cannot clear it.
                resolution[candidate] = resolution.TryGetValue(candidate, out string? existing) && existing != "Resolved"
                    ? existing
                    : relationship.Resolution == "Resolved" ? "Resolved" : relationship.Resolution;
            }
        }
        SourceService[] affected = [.. byService
            .Select(entry => new SourceService(entry.Key, [.. entry.Value], resolution[entry.Key], resolvedAt))
            .OrderBy(service => service.Name, StringComparer.OrdinalIgnoreCase)];

        SourceWorkWindow? work = await WindowAsync(ocoReference, warnings, token);

        bool partial = !membership.Complete || failed > 0 || ambiguous > 0
            || membership.Devices.Count == 0 || work is null || work.Resolution != "Resolved";
        if (membership.Devices.Count == 0)
        { warnings.Add("CollectionHadNoDevices"); }
        if (ambiguous > 0)
        { warnings.Add($"AmbiguousDeviceRelationships:{ambiguous}"); }
        if (missing > 0)
        { warnings.Add($"DevicesWithoutService:{missing}"); }
        if (failed > 0)
        { warnings.Add($"DeviceRelationshipReadFailures:{failed}"); }
        logger.LogInformation("Announcement source snapshot built. JobId: {JobId}. Devices: {Devices}. Services: {Services}. Partial: {Partial}.",
            jobId, membership.Devices.Count, affected.Length, partial);

        return new AnnouncementSourceSnapshot(jobId, draftId, owner, profile, ocoReference, resolvedAt, collectionId,
            membership.Devices, affected, work,
            new SourceCompleteness(membership.Complete, membership.Devices.Count, membership.PagesRead,
                membership.Devices.Count, affected.Count(service => service.Resolution == "Resolved"),
                ambiguous, missing, failed, partial, [.. warnings.Distinct(StringComparer.Ordinal)]));
    }

    private async Task<ServiceLookupResult> LookupAsync(string device, AnnouncementSourceOptions settings, CancellationToken token)
    {
        using var perDevice = CancellationTokenSource.CreateLinkedTokenSource(token);
        perDevice.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(settings.DeviceLookupTimeoutSeconds, 5, 300)));
        try
        { return await services.GetDeviceServicesAsync(device, perDevice.Token); }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        { return new(device, [], "Failed"); }
        catch (AnnouncementSourceException exception) when (exception.ErrorCode != "AnnouncementSourceConfigurationUnavailable")
        {
            // One unreadable device must not discard the devices that did resolve.
            return new(device, [], "Failed");
        }
    }

    private async Task<SourceWorkWindow?> WindowAsync(string ocoReference, List<string> warnings, CancellationToken token)
    {
        ChangeWindowResult window;
        try
        { window = await services.GetChangeWindowAsync(ocoReference, token); }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        { warnings.Add("ChangeWindowTimeout"); return null; }
        catch (AnnouncementSourceException exception)
        { warnings.Add("ChangeWindowUnavailable:" + exception.ErrorCode); return null; }
        if (window.Resolution != "Resolved")
        { warnings.Add("ChangeWindow" + window.Resolution); }
        // The source states a local wall-clock value with no offset. The date part is reported as a
        // source-local date; no instant, offset or restart time is derived from either string.
        string? startDate = window.StartText is { Length: >= 10 } text
            && DateOnly.TryParseExact(text[..10], "yyyy-MM-dd", out DateOnly parsed)
            ? parsed.ToString("yyyy-MM-dd") : null;
        return new SourceWorkWindow(window.StartText, window.FinishText, startDate, "Unresolved",
            window.Resolution, time.GetUtcNow());
    }
}
