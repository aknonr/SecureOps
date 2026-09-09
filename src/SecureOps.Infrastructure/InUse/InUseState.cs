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
                return server with { Fields = fields };
            }).ToArray()
            };
            InUseServer[] absent = old.Source.Servers.Where(s => !source.Servers.Any(n => n.Id == s.Id)).ToArray();
            if (absent.Length > 0 || missingFields)
            {
                InUseServer[] retained = source.Servers.Concat(absent).OrderBy(s => s.Id, StringComparer.Ordinal).ToArray();
                source = source with
                {
                    Servers = retained.Length <= 100 ? retained : old.Source.Servers,
                    ServiceItemsState = "Partial",
                    RelationshipEvidence = "Observed result omitted previously stored service items or fields; prior evidence retained. Completeness and current values require verification."
                };
            }
        }
        else if (old is not null && source.ServiceItemsState != "Complete")
        { source = source with { Servers = old.Source.Servers, ServiceOwner = old.Source.ServiceOwner, ProvisioningTeam = old.Source.ProvisioningTeam }; }
        if (old is not null && source.AffectedAssetsState != "Complete")
        { source = source with { AffectedAssetCount = old.Source.AffectedAssetCount }; }
        string hash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(source)));
        if (old is null)
        {
            return new(Guid.NewGuid(), source, hash, 1, 1, null, null, null, now);
        }
        bool changed = hash != old.SourceHash;
        return old with
        {
            Source = source,
            SourceHash = hash,
            SourceVersion = old.SourceVersion + (changed ? 1 : 0),
            Version = old.Version + 1,
            LastSeenAt = now
        };
    }

    internal static InUseRefreshState Refresh(InUseRefreshState old, InUseBatch? batch, string? error, DateTimeOffset now) =>
        new(old.Version + 1, now, batch is null ? old.LastSuccessfulAt : now,
            batch?.Complete ?? false, error ?? batch?.Issue);

    internal static bool Valid(InUseBatch batch) => batch.Records.Count <= 100
        && batch.Records.Select(r => r.Id).Distinct(StringComparer.Ordinal).Count() == batch.Records.Count
        && batch.Records.All(r => !string.IsNullOrWhiteSpace(r.Id) && r.Id.Length <= 100 && r.Code.Length <= 100
            && r.Title.Length <= 1000 && r.Servers.Count <= 100
            && ValidState(r.ServiceItemsState) && ValidState(r.AffectedAssetsState)
            && r.AffectedAssetCount is not < 0
            && r.Servers.Select(s => s.Id).Distinct(StringComparer.Ordinal).Count() == r.Servers.Count
            && r.Servers.All(s => s.Id.Length is > 0 and <= 100 && s.Fields.Count <= 50
                && s.Fields.All(f => f.Key.Length <= 100 && f.Value.Value?.Length is not > 1000 && f.Value.Source.Length <= 300)));

    private static bool ValidState(string state) => state is "Complete" or "Observed" or "Partial" or "NotQueried" or "Forbidden" or "Failed" or "Ambiguous";
}
