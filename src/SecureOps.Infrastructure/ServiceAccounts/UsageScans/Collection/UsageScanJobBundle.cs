using System.Text.Json;
using SecureOps.Domain.ServiceAccounts;

namespace SecureOps.Infrastructure.ServiceAccounts.UsageScans.Collection;

/// <summary>
/// Builds the <c>service-account-usage-scan-v1</c> bundle (<c>tool = Jea</c>) the module stores as evidence, from collector documents.
/// The same strict <see cref="UsageScanParser"/> as an uploaded file validates it, so a web scan can never store more than an upload.
/// </summary>
public static class UsageScanJobBundle
{
    /// <summary>
    /// The bundle for the given planned servers. <paramref name="documents"/> holds the Ok servers' documents; every other planned
    /// server goes to <c>notReached</c> with <see cref="UsageScanJobRules.EvidenceReason"/> of its status (never dropped).
    /// </summary>
    public static byte[] Build(IReadOnlyList<string> accounts, string? expectedAccount, IReadOnlyList<string> plannedServers,
        IReadOnlyDictionary<string, byte[]> documents, IReadOnlyDictionary<string, UsageCollectionStatus> statuses, DateTimeOffset generatedAt)
    {
        using MemoryStream buffer = new();
        using (Utf8JsonWriter json = new(buffer))
        {
            json.WriteStartObject();
            json.WriteString("schema", "service-account-usage-scan-v1");
            json.WriteString("generatedAt", generatedAt.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", System.Globalization.CultureInfo.InvariantCulture));
            json.WriteString("tool", "Jea");
            json.WriteStartArray("accounts");
            foreach (string account in accounts)
            {
                json.WriteStringValue(account);
            }

            json.WriteEndArray();
            if (expectedAccount is null)
            {
                json.WriteNull("expectedAccount");
            }
            else
            {
                json.WriteString("expectedAccount", expectedAccount);
            }

            json.WriteStartArray("plannedServers");
            foreach (string server in plannedServers)
            {
                json.WriteStringValue(server);
            }

            json.WriteEndArray();
            json.WriteStartArray("results");
            foreach (string server in plannedServers)
            {
                if (documents.TryGetValue(server, out byte[]? document))
                {
                    json.WriteRawValue(document, skipInputValidation: false);
                }
            }

            json.WriteEndArray();
            json.WriteStartArray("notReached");
            foreach (string server in plannedServers.Where(s => !documents.ContainsKey(s)))
            {
                json.WriteStartObject();
                json.WriteString("serverName", server);
                json.WriteString("reason", UsageScanJobRules.EvidenceReason(statuses.TryGetValue(server, out UsageCollectionStatus status)
                    ? status : UsageCollectionStatus.NotRun).ToString());
                json.WriteEndObject();
            }

            json.WriteEndArray();
            json.WriteEndObject();
        }

        return buffer.ToArray();
    }

    /// <summary>
    /// Validates one server's document on its own, as a one-server bundle through <see cref="UsageScanParser"/> (secret guard first).
    /// Returns the parsed server and its items, or the parser's stable refusal code. The document content is never logged.
    /// </summary>
    public static (ParsedUsageScan? Parsed, string? RefusalCode) Validate(IReadOnlyList<string> accounts, string? expectedAccount, string server,
        byte[] document, int maxDocumentBytes, DateTimeOffset now)
    {
        if (document.Length == 0 || document.Length > maxDocumentBytes)
        {
            return (null, document.Length == 0 ? UsageScanFileCodes.Empty : UsageScanFileCodes.TooLarge);
        }

        try
        {
            byte[] bundle = Build(accounts, expectedAccount, [server], new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase) { [server] = document },
                new Dictionary<string, UsageCollectionStatus>(StringComparer.OrdinalIgnoreCase), now);
            return (UsageScanParser.Parse(bundle, int.MaxValue, now), null);
        }
        catch (UsageScanFileException refused)
        {
            return (null, refused.Code);
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or InvalidOperationException)
        {
            // WriteRawValue refuses text that is not one JSON value before the parser sees it.
            return (null, UsageScanFileCodes.Json);
        }
    }
}
