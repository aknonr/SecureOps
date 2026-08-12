using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace SecureOps.Infrastructure.Commands;

/// <summary>Deterministic fallback and validation for command keys.</summary>
public static partial class CommandIdempotency
{
    /// <summary>Creates a deterministic actor-scoped fallback key.</summary>
    public static string Create(string actor, string commandName, string targetId)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{actor.Trim().ToLowerInvariant()}\n{commandName}\n{targetId}"));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>Returns whether a caller-supplied key is bounded and contains safe opaque characters.</summary>
    public static bool IsValid(string value, int maximumLength) => value.Length is >= 16 && value.Length <= maximumLength && SafeKeyRegex().IsMatch(value);

    [GeneratedRegex("^[A-Za-z0-9._:-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeKeyRegex();
}
