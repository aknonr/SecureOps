using System.Text;
using SecureOps.Shared.Contracts.ServiceAccounts;

namespace SecureOps.Ui.Services.ServiceAccounts;

/// <summary>
/// The planned servers a person should scan again because this scan has no usable result for them (partial, failed,
/// unreachable or no result), in the collector's <c>-ComputerListPath</c> format. Built in the browser from the scan's server
/// list, which the detail always carries in full; nothing is sent anywhere.
/// </summary>
public static class ServiceAccountRescanList
{
    /// <summary>Servers without a usable result, ordered by name.</summary>
    public static IReadOnlyList<string> Servers(UsageScanView scan) =>
        [.. scan.Servers.Where(s => s.Outcome is not ("Found" or "NotFound")).Select(s => s.ServerName).Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)];

    /// <summary>One name per line with <c>#</c> comment lines (CRLF).</summary>
    public static string File(UsageScanView scan)
    {
        StringBuilder text = new();
        text.Append("# Yeniden taranacak sunucular (önceki taramada tam sonuç gelmeyenler)\r\n");
        text.Append("# Kaynak tarama dosyası: ").Append(scan.FileName).Append("\r\n");
        foreach (string name in Servers(scan))
        {
            text.Append(name).Append("\r\n");
        }

        return text.ToString();
    }
}
