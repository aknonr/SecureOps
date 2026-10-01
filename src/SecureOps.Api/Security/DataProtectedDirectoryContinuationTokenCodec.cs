using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using SecureOps.Infrastructure.DirectoryExplorer;
using SecureOps.Shared.Configuration;

namespace SecureOps.Api.Security;

/// <summary>Protects Directory Explorer continuation state with persistent Data Protection.</summary>
public sealed class DataProtectedDirectoryContinuationTokenCodec : IDirectoryContinuationTokenCodec
{
    private const string _purpose = "SecureOps.DirectoryExplorer.ContinuationToken.v1";
    private readonly IDataProtector _protector;
    private readonly DirectoryExplorerOptions _options;
    private readonly TimeProvider _timeProvider;

    /// <summary>Initializes a purpose-isolated codec.</summary>
    public DataProtectedDirectoryContinuationTokenCodec(
        IDataProtectionProvider provider,
        IOptions<DirectoryExplorerOptions> options,
        TimeProvider timeProvider)
    {
        _protector = provider.CreateProtector(_purpose);
        _options = options.Value;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public string Create(string operation, string normalizedTarget, int offset)
    {
        TokenPayload payload = new(
            1,
            operation,
            offset,
            _timeProvider.GetUtcNow().AddSeconds(_options.ContinuationTokenLifetimeSeconds).ToUnixTimeSeconds(),
            TargetHash(normalizedTarget));
        return _protector.Protect(JsonSerializer.Serialize(payload));
    }

    /// <inheritdoc />
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

        try
        {
            TokenPayload? payload = JsonSerializer.Deserialize<TokenPayload>(_protector.Unprotect(token));
            if (payload is null
                || payload.Version != 1
                || !string.Equals(payload.Operation, operation, StringComparison.Ordinal)
                || payload.Offset < 0
                || payload.ExpiresAtUnixSeconds < _timeProvider.GetUtcNow().ToUnixTimeSeconds()
                || !string.Equals(payload.TargetHash, TargetHash(normalizedTarget), StringComparison.Ordinal))
            {
                return false;
            }

            offset = payload.Offset;
            return true;
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException or FormatException)
        {
            return false;
        }
    }

    private static string TargetHash(string target) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(target))).ToLowerInvariant();

    private sealed record TokenPayload(int Version, string Operation, int Offset, long ExpiresAtUnixSeconds, string TargetHash);
}
