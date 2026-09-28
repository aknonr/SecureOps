using System.Security.Cryptography;
using System.Text.Json;
using SecureOps.Shared.Contracts.InUse;

namespace SecureOps.Infrastructure.InUse;

/// <summary>Deterministic merge rules shared by local and SQL persistence.</summary>
internal static class InUseState
{
    internal static InUseRecord Merge(InUseRecord? old, InUseSource source, DateTimeOffset now)
    {
        if (old is not null && source.ServiceItemsState == "Observed")
        {
            bool missingFields = false;
            source = source with
            {
                Servers = source.Servers.Select(server =>
            {
                InUseServer? previous = old.Source.Servers.FirstOrDefault(s => s.Id == server.Id);
                var fields = server.Fields.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
                foreach (KeyValuePair<string, InUseEvidence> field in server.Fields.Where(f => f.Value.Source.StartsWith("Missing response cell: ", StringComparison.Ordinal)))
                {
                    if (previous?.Fields.GetValueOrDefault(field.Key)?.Value is string retainedValue)
                    {
                        fields[field.Key] = new(retainedValue, "Prior value retained; " + field.Value.Source);
                        missingFields = true;
                    }
                }
                return server with
                {
                    Fields = fields,
                    RelatedRequestReporter = MergeReporter(server.RelatedRequestReporter, previous?.RelatedRequestReporter)
                };
            }).ToArray()
            };
            InUseServer[] absent = old.Source.Servers.Where(s => !source.Servers.Any(n => n.Id == s.Id)).ToArray();
            if (absent.Length > 0 || missingFields)
            {
                InUseServer[] retained = source.Servers.Concat(absent.Select(s => s with
                { RelatedRequestReporter = Stale(s.RelatedRequestReporter) })).OrderBy(s => s.Id, StringComparer.Ordinal).ToArray();
                source = source with
                {
                    Servers = retained.Length <= 100 ? retained : old.Source.Servers.Select(s => s with
                    { RelatedRequestReporter = Stale(s.RelatedRequestReporter) }).ToArray(),
                    ServiceItemsState = "Partial",
                    RelationshipEvidence = "Observed result omitted previously stored service items or fields; prior evidence retained. Completeness and current values require verification."
                };
            }
        }
        else if (old is not null && source.ServiceItemsState != "Complete")
        { source = source with { Servers = old.Source.Servers.Select(s => s with { RelatedRequestReporter = Stale(s.RelatedRequestReporter) }).ToArray(), ServiceOwner = old.Source.ServiceOwner, ProvisioningTeam = old.Source.ProvisioningTeam }; }
        if (old is not null && source.AffectedAssetsState != "Complete")
        { source = source with { AffectedAssetCount = old.Source.AffectedAssetCount }; }
        // Verification time is freshness metadata; unchanged source content must not invalidate a draft.
        InUseSource fingerprint = Canonical(source);
        string hash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(fingerprint)));
        if (old is null)
        {
            return new(Guid.NewGuid(), source, hash, 1, 1, null, null, null, now) { FirstSeenAt = now };
        }
        bool changed = hash != old.SourceHash;
        return old with
        {
            Source = source,
            SourceHash = hash,
            SourceVersion = old.SourceVersion + (changed ? 1 : 0),
            ReviewSourceVersion = ReviewHash(old.Source) == ReviewHash(source)
                ? old.ReviewSourceVersion ?? old.SourceVersion : old.SourceVersion + (changed ? 1 : 0),
            SourceChanges = changed ? ParentChanges(old.Source, source) : old.SourceChanges,
            Version = old.Version + 1,
            SourceObservationMissing = false,
            LastSeenAt = now
        };
    }

    private static InUseSource Canonical(InUseSource source) => source with
    {
        Servers = source.Servers.OrderBy(s => s.Id, StringComparer.Ordinal).Select(s => s with
        {
            Fields = new SortedDictionary<string, InUseEvidence>(s.Fields.ToDictionary(f => f.Key, f => f.Value), StringComparer.Ordinal),
            RelatedRequestReporter = s.RelatedRequestReporter is null ? null : s.RelatedRequestReporter with { LastVerifiedAt = null }
        }).ToArray()
    };

    // Parent stage/title and reporter freshness still fence execution, but do not invalidate server answers.
    private static string ReviewHash(InUseSource source) => JsonSerializer.Serialize(new
    {
        source.Id,
        source.IdentityScope,
        source.Synthetic,
        source.ServiceItemsState,
        source.ServiceOwner,
        source.ProvisioningTeam,
        Servers = Canonical(source).Servers.Select(s => new
        { s.Id, Fields = s.Fields.Where(f => InUseSourceChanges.AffectsReview(f.Key)).ToDictionary(f => f.Key, f => f.Value, StringComparer.Ordinal) })
    });

    private static InUseFieldChange[] ParentChanges(InUseSource before, InUseSource after) => new[]
    {
        new InUseFieldChange("Execution", "OR başlığı", before.Title, after.Title),
        new InUseFieldChange("Execution", "OR talep eden", before.Requester.Value, after.Requester.Value),
        new InUseFieldChange("Workflow", "Genel OR durumu", before.Lifecycle?.Value, after.Lifecycle?.Value),
        new InUseFieldChange("Workflow", "WASAS aktivitesi", before.WasasActivity?.Value, after.WasasActivity?.Value),
        new InUseFieldChange("Workflow", "Güncel ekip / aşama", before.CurrentStage?.Value, after.CurrentStage?.Value),
        new InUseFieldChange("Review", "Sunucu ilişkisi", before.ServiceItemsState, after.ServiceItemsState),
        new InUseFieldChange("Review", "Servis sahibi", before.ServiceOwner.Value, after.ServiceOwner.Value),
        new InUseFieldChange("Review", "Kurulum ekibi", before.ProvisioningTeam.Value, after.ProvisioningTeam.Value)
    }.Where(c => c.Before != c.After).ToArray();

    internal static InUseRefreshState Refresh(InUseRefreshState old, InUseBatch? batch, string? error, DateTimeOffset now) =>
        new(old.Version + 1, now, batch is null ? old.LastSuccessfulAt : now,
            batch?.Complete ?? false, error ?? batch?.Issue);

    internal static InUseRecord RetainUnobserved(InUseRecord old)
    {
        if (old.SourceObservationMissing && !old.Source.Servers.Any(s => s.RelatedRequestReporter is { State: not "Stale" }))
        { return old; }
        InUseSource source = old.Source with
        {
            Servers = old.Source.Servers.Select(s => s with
            { RelatedRequestReporter = Stale(s.RelatedRequestReporter) }).ToArray()
        };
        return Merge(old, source, old.LastSeenAt) with
        { LastSeenAt = old.LastSeenAt, ReviewSourceVersion = old.ReviewSourceVersion, SourceObservationMissing = true };
    }

    internal static bool Valid(InUseBatch batch) => batch.Records.Count <= 100
        && batch.Records.Select(r => r.Id).Distinct(StringComparer.Ordinal).Count() == batch.Records.Count
        && batch.Records.All(r => !string.IsNullOrWhiteSpace(r.Id) && r.Id.Length <= 100 && r.Code.Length <= 100
            && r.Title.Length <= 1000 && r.Servers.Count <= 100
            && ValidState(r.ServiceItemsState) && ValidState(r.AffectedAssetsState)
            && r.AffectedAssetCount is not < 0
            && r.Servers.Select(s => s.Id).Distinct(StringComparer.Ordinal).Count() == r.Servers.Count
            && r.Servers.All(s => s.Id.Length is > 0 and <= 100 && s.Fields.Count <= 50
                && ValidReporter(s.RelatedRequestReporter, r.Id, s.Id)
                && s.Fields.All(f => f.Key.Length <= 100 && f.Value.Value?.Length is not > 1000 && f.Value.Source.Length <= 300)));

    private static InUseRelatedRequestReporter? Stale(InUseRelatedRequestReporter? reporter) => reporter is null ? null
        : reporter with { State = "Stale" };

    private static InUseRelatedRequestReporter? MergeReporter(InUseRelatedRequestReporter? next, InUseRelatedRequestReporter? old)
    {
        if (next is null)
        { return Stale(old); }
        if (next.State == "Stale" && next.RfcReference is null && old is not null)
        { return Stale(old); }
        if (next.State == "ExactMatch" && (next.DisplayState == "Omitted" || next.ReferenceState == "Omitted"))
        { next = next with { State = "Stale", LastVerifiedAt = null }; }
        if (next.State == "ExactMatch" || old is null || next.RfcReference != old.RfcReference
            || next.ReferenceKind != old.ReferenceKind)
        { return next; }
        return next with
        {
            RequestId = old.RequestId,
            RequestCode = old.RequestCode,
            Display = old.Display,
            UserReference = old.UserReference,
            LastVerifiedAt = old.LastVerifiedAt
        };
    }

    private static bool ValidReporter(InUseRelatedRequestReporter? value, string parent, string server) => value is null
        || value.ParentId == parent && value.ServiceItemId == server
        && value.ReferenceKind is "SourceId" or "OrCode"
        && value.State is "ExactMatch" or "MissingRfc" or "NotFoundOrNotVisible" or "AmbiguousMatch" or "IdentityMismatch"
            or "Forbidden" or "Failed" or "Stale" or "NotQueried"
        && value.DisplayState is "Returned" or "Empty" or "Null" or "Omitted"
        && value.ReferenceState is "Returned" or "Empty" or "Null" or "Omitted"
        && value.RfcReference?.Length is not > 254 && value.RequestId?.Length is not > 100
        && value.RequestCode?.Length is not > 100 && value.Display?.Length is not > 1000 && value.UserReference?.Length is not > 100
        && (value.State != "ExactMatch" || value.LastVerifiedAt is not null && !string.IsNullOrWhiteSpace(value.RequestId)
            && !string.IsNullOrWhiteSpace(value.RequestCode) && !string.IsNullOrWhiteSpace(value.RfcReference));

    private static bool ValidState(string state) => state is "Complete" or "Observed" or "Partial" or "NotQueried" or "Forbidden" or "Failed" or "Ambiguous";
}
