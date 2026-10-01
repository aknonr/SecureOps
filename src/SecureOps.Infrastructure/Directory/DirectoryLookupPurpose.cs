namespace SecureOps.Infrastructure.DirectoryExplorer;

/// <summary>Normalizes optional privacy-safe context for read-only directory queries.</summary>
internal static class DirectoryLookupPurpose
{
    /// <summary>Trims a supplied purpose and validates its configured bound.</summary>
    public static bool TryNormalize(string? purpose, int maxLength, out string? normalized)
    {
        if (string.IsNullOrWhiteSpace(purpose))
        {
            normalized = null;
            return true;
        }

        normalized = purpose.Trim();
        return normalized.Length <= maxLength && !purpose.Any(char.IsControl);
    }
}
