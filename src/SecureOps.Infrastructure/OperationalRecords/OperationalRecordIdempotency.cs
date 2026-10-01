using System.Security.Cryptography;
using System.Text;

namespace SecureOps.Infrastructure.OperationalRecords;

/// <summary>Creates deterministic non-secret Jira transfer identifiers.</summary>
public static class OperationalRecordIdempotency
{
    /// <summary>Creates an idempotency key from source identity and mapping version.</summary>
    public static string Create(string sourceRecordId, string mappingVersion)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{sourceRecordId}\n{mappingVersion}"));
        return Convert.ToHexString(bytes);
    }
}
