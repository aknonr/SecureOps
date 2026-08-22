using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using SecureOps.Shared.Configuration;

namespace SecureOps.Infrastructure.DirectoryExplorer;

/// <summary>Creates opaque, integrity-protected, expiring page tokens.</summary>
public sealed class DirectoryContinuationTokenCodec
{
    private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);
    private readonly DirectoryExplorerOptions _options;
    private readonly TimeProvider _timeProvider;

    /// <summary>Initializes the codec with a process-local key.</summary>
    public DirectoryContinuationTokenCodec(IOptions<DirectoryExplorerOptions> options, TimeProvider timeProvider)
    {
        _options = options.Value;
        _timeProvider = timeProvider;
    }

    /// <summary>Creates a target-bound continuation token.</summary>
    public string Create(string operation, string normalizedTarget, int offset)
    {
        long expires = _timeProvider.GetUtcNow().AddSeconds(_options.ContinuationTokenLifetimeSeconds).ToUnixTimeSeconds();
        string payload = $"1|{operation}|{offset.ToString(CultureInfo.InvariantCulture)}|{expires.ToString(CultureInfo.InvariantCulture)}|{TargetHash(normalizedTarget)}";
        byte[] signature = HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(payload));
        return Base64Url(Encoding.UTF8.GetBytes(payload)) + "." + Base64Url(signature);
    }

    /// <summary>Validates a token and returns its next offset.</summary>
    public bool TryRead(string? token, string operation, string normalizedTarget, out int offset)
    {
        offset = 0;
        if (string.IsNullOrWhiteSpace(token))
        {
            return true;
        }

        if (token.Length > 1024)
        {
            return false;
        }

        string[] tokenParts = token.Split('.');
        if (tokenParts.Length != 2 || !TryBase64Url(tokenParts[0], out byte[] payloadBytes) || !TryBase64Url(tokenParts[1], out byte[] signature))
        {
            return false;
        }

        byte[] expected = HMACSHA256.HashData(_key, payloadBytes);
        if (!CryptographicOperations.FixedTimeEquals(expected, signature))
        {
            return false;
        }

        string[] parts = Encoding.UTF8.GetString(payloadBytes).Split('|');
        return parts.Length == 5
            && parts[0] == "1"
            && string.Equals(parts[1], operation, StringComparison.Ordinal)
            && int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out offset)
            && offset >= 0
            && long.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out long expires)
            && expires >= _timeProvider.GetUtcNow().ToUnixTimeSeconds()
            && string.Equals(parts[4], TargetHash(normalizedTarget), StringComparison.Ordinal);
    }

    private static string TargetHash(string target) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(target))).ToLowerInvariant();

    private static string Base64Url(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static bool TryBase64Url(string value, out byte[] bytes)
    {
        try
        {
            string padded = value.Replace('-', '+').Replace('_', '/');
            padded += new string('=', (4 - padded.Length % 4) % 4);
            bytes = Convert.FromBase64String(padded);
            return true;
        }
        catch (FormatException)
        {
            bytes = [];
            return false;
        }
    }
}
