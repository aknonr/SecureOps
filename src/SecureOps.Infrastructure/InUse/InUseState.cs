using System.Security.Cryptography;
using System.Text.Json;
using SecureOps.Shared.Contracts.InUse;

namespace SecureOps.Infrastructure.InUse;

/// <summary>Deterministic merge rules shared by local and SQL persistence.</summary>
internal static class InUseState
{
    internal static InUseRecord Merge(InUseRecord? old, InUseSource source, DateTimeOffset now)
    {
        if (old is not null && source.ServiceItemsState != "Complete")
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

    private static bool ValidState(string state) => state is "Complete" or "NotQueried" or "Forbidden" or "Failed" or "Ambiguous";
}
