using System.Net;

namespace SecureOps.Shared.Contracts.InUse;

/// <summary>Single display-boundary decoding. Never use the result as HTML or persisted source evidence.</summary>
public static class InUseDisplayText
{
    /// <summary>Requester and service display values retain source encoding in storage, including old records.</summary>
    public static string Decode(string? value) => value is null ? "Bilinmiyor" : WebUtility.HtmlDecode(value);

    /// <summary>Keep source/reference identifiers byte-for-byte, decode only display fields.</summary>
    public static string Field(string key, string? value) => key.StartsWith("Reference: ", StringComparison.Ordinal)
        || key is "ENVANTER_ID" or "ITMC_Service_ID" ? value ?? "Unknown" : value is null ? "Unknown" : Decode(value);
}
