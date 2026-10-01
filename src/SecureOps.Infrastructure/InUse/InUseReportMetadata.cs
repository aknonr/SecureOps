using System.Security.Cryptography;
using System.Text.Json;
using SecureOps.Shared.Contracts.InUse;

namespace SecureOps.Infrastructure.InUse;

/// <summary>Metadata projection from the selected envelope only; no current record/profile fallback.</summary>
public sealed record InUseReportMetadata(string? SourceCode, string HostsJson, string? PreparedByAccount,
    string DownloadName, string MetadataHash)
{
    /// <summary>Extracts bounded trusted archive fields without reading or rewriting XLSX contents.</summary>
    public static InUseReportMetadata From(InUseReport report)
    {
        string? code = report.SourceCode ?? report.Sheets.Concat(report.EvidenceSheets)
            .Where(s => s.Name == "Provenance").SelectMany(s => s.Rows)
            .FirstOrDefault(r => r.Count == 2 && r[0] == "Code")?[1];
        if (string.IsNullOrWhiteSpace(code))
        { code = null; }
        string[] hosts = report.Sheets.Where(s => s.Name == "Sunucular").SelectMany(s => s.Rows)
            .Where(r => r.Count > 1 && r[0] == "HOSTNAME").SelectMany(r => r.Skip(1))
            .Where(h => !string.IsNullOrWhiteSpace(h)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (code?.Length > 128 || report.PreparedByAccount?.Length > 256 || hosts.Length > 100 || hosts.Any(h => h.Length > 256))
        { throw new InvalidDataException("Archive catalogue metadata exceeds its bound."); }
        string json = JsonSerializer.Serialize(hosts);
        string name = InUseReportNames.Download(report);
        string hash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        { code, hosts, report.PreparedByAccount, report.PreparedBy, report.PreparedByLabel, report.PreparedAt, report.Sha256, name })));
        return new(code, json, report.PreparedByAccount, name, hash);
    }
}
