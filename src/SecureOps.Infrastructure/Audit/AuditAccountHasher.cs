using System.Security.Cryptography;
using System.Text;

namespace SecureOps.Infrastructure.Audit;

/// <summary>
/// Creates stable hashes for submitted account inputs without storing raw rejected values.
/// </summary>
public static class AuditAccountHasher
{
    /// <summary>
    /// Hashes an account input for audit correlation.
    /// </summary>
    /// <param name="account">Account input.</param>
    /// <returns>SHA-256 hash of the trimmed lower-case value, or null when no account was provided.</returns>
    public static string? HashAccountInput(string? account)
    {
        if (string.IsNullOrWhiteSpace(account))
        {
            return null;
        }

        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(account.Trim().ToLowerInvariant()));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
