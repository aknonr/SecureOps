using System.Text.Json;

namespace SecureOps.Infrastructure.OperationalRecords;

/// <summary>Bounded operator JSON files may contain a single UTF-8 byte order mark.</summary>
public static class InUseDiagnosticJson
{
    /// <summary>Parses without changing the original bytes used for provenance hashes.</summary>
    public static T Read<T>(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length > 4096)
        { throw new InvalidDataException("Diagnostic input exceeds the bound."); }
        if (bytes.StartsWith(new byte[] { 0xef, 0xbb, 0xbf }))
        { bytes = bytes[3..]; }
        return JsonSerializer.Deserialize<T>(bytes) ?? throw new InvalidDataException("Diagnostic input is null.");
    }
}
