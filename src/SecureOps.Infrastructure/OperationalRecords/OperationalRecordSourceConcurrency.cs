using System.Security.Cryptography;
using System.Text;

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

        string canonical = string.Join('\n',
            item.SourceRecordId,
            item.OrCode,
            item.Title,
            item.Description,
            item.Requester ?? string.Empty,
            item.CreatedAt.ToUniversalTime().ToString("O"),
            item.Environment ?? string.Empty,
            item.ServerReference ?? string.Empty,
            item.ApplicationReference ?? string.Empty,
            item.IsOpen ? "open" : "closed",
            item.LastModifiedAt?.ToUniversalTime().ToString("O") ?? string.Empty);
        return $"sha256:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant()}";
    }
}
