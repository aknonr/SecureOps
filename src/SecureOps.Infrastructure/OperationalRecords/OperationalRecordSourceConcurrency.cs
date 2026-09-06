using System.Security.Cryptography;
using System.Text.Json;

namespace SecureOps.Infrastructure.OperationalRecords;

/// <summary>Builds the strongest deterministic source-state token available.</summary>
public static class OperationalRecordSourceConcurrency
{
    /// <summary>Uses an explicit source token, otherwise hashes the bounded source projection.</summary>
    public static string Create(OperationalRecordSourceItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.VersionToken))
        {
            return $"source:{item.VersionToken.Trim()}";
        }

        byte[] canonical = JsonSerializer.SerializeToUtf8Bytes(new object?[]
        {
            "source-state-v2",
            item.SourceRecordId,
            item.OrCode,
            item.Title,
            item.Description,
            item.Requester,
            item.CreatedAt?.ToUniversalTime(),
            item.Environment,
            item.ServerReference,
            item.ApplicationReference,
            item.IsOpen,
            item.LastModifiedAt?.ToUniversalTime()
        });
        return $"sha256:{Convert.ToHexString(SHA256.HashData(canonical)).ToLowerInvariant()}";
    }
}
