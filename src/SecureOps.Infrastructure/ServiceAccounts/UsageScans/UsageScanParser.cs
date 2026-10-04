using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using SecureOps.Domain.ServiceAccounts;

namespace SecureOps.Infrastructure.ServiceAccounts.UsageScans;

/// <summary>A usage-scan upload was refused; <see cref="Code"/> is a stable, content-free reason.</summary>
public sealed class UsageScanFileException(string code) : Exception(code)
{
    /// <summary>Stable reason code (no file content).</summary>
    public string Code { get; } = code;
}

/// <summary>Stable rejection codes for usage-scan uploads.</summary>
public static class UsageScanFileCodes
{
    /// <summary>Empty file.</summary>
    public const string Empty = "scanEmpty";
    /// <summary>Above the configured size.</summary>
    public const string TooLarge = "scanTooLarge";
    /// <summary>Not UTF-8 text.</summary>
    public const string Encoding = "scanEncoding";
    /// <summary>Not valid JSON, or too deeply nested.</summary>
    public const string Json = "scanJson";
    /// <summary>The same property twice in one object.</summary>
    public const string DuplicateField = "scanDuplicateField";
    /// <summary>A property name looks like a password or other secret.</summary>
    public const string SecretField = "scanSecretField";
    /// <summary>A text value looks like an embedded password assignment.</summary>
    public const string SecretValue = "scanSecretValue";
    /// <summary>Not the upload contract (unknown property, wrong type, enum, pattern or length).</summary>
    public const string Schema = "scanSchema";
    /// <summary>Above a count limit.</summary>
    public const string Limits = "scanLimits";
    /// <summary>Planned servers, results and not-reached servers do not add up.</summary>
    public const string Servers = "scanServers";
    /// <summary>A document disagrees with the bundle or with itself.</summary>
    public const string Inconsistent = "scanInconsistent";
    /// <summary>A timestamp lies in the future.</summary>
    public const string FutureDate = "scanFutureDate";
}

/// <summary>One planned server as stored.</summary>
public sealed record ParsedScanServer(string ServerName, ScanServerResult Result, string? WindowsServices, string? ScheduledTasks, string? Iis,
    DateTimeOffset? ScannedAt, string? Warnings);

/// <summary>One matched component as stored; <c>Role</c> is <c>Former</c> (a searched account) or <c>Expected</c> (the gMSA).</summary>
public sealed record ParsedScanItem(string ServerName, string Role, string MatchedAccount, string ComponentType, string ComponentName, string Identity,
    string? State, string? Detail);

/// <summary>A validated upload.</summary>
public sealed record ParsedUsageScan(string Tool, DateTimeOffset CombinedAt, IReadOnlyList<string> Accounts, string? ExpectedAccount,
    IReadOnlyList<ParsedScanServer> Servers, IReadOnlyList<ParsedScanItem> Items)
{
    /// <summary><c>GmsaCheck</c> when an expected gMSA was given, otherwise <c>Discovery</c>.</summary>
    public string Purpose => ExpectedAccount is null ? "Discovery" : "GmsaCheck";

    /// <summary>Servers that returned a document.</summary>
    public int AnsweredServers => Servers.Count(s => s.ScannedAt is not null);

    /// <summary>Earliest server scan time, if any server answered.</summary>
    public DateTimeOffset? FirstScannedAt => Servers.Where(s => s.ScannedAt is not null).Select(s => s.ScannedAt).Min();

    /// <summary>Latest server scan time, if any server answered.</summary>
    public DateTimeOffset? LastScannedAt => Servers.Where(s => s.ScannedAt is not null).Select(s => s.ScannedAt).Max();
}

/// <summary>
/// Strict, fail-closed reader of <c>service-account-usage-scan-v1</c> (ADR-0027). The secret guard runs over the whole document
/// before any field is interpreted; a refused file is never stored or echoed. Only the closed contract is accepted.
/// </summary>
public static partial class UsageScanParser
{
    /// <summary>Allowed clock skew for scan timestamps.</summary>
    public static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(10);

    /// <summary>Most planned servers per file (same as the operator script).</summary>
    public const int MaxServers = 500;
    /// <summary>Most components per server.</summary>
    public const int MaxComponentsPerServer = 2000;
    /// <summary>Most stored items per file.</summary>
    public const int MaxItems = 10000;
    /// <summary>Most warnings per server.</summary>
    public const int MaxWarnings = 20;

    private const int _maxDepth = 12;
    private const string _bundleSchema = "service-account-usage-scan-v1";
    private const string _documentSchema = "service-account-usage-v1";
    private static readonly string[] _secretWords =
        ["password", "passwd", "pwd", "secret", "credential", "token", "apikey", "privatekey", "connectionstring", "parola", "sifre"];
    private static readonly string[] _bundleFields = ["schema", "generatedAt", "tool", "accounts", "expectedAccount", "plannedServers", "results", "notReached"];
    private static readonly string[] _documentRequired = ["schema", "serverName", "generatedAt", "durationMs", "accounts", "scanResult", "sources", "components", "warnings"];
    private static readonly string[] _documentFields = [.. _documentRequired, "verification"];
    private static readonly string[] _sourceFields = ["WindowsServices", "ScheduledTasks", "Iis"];
    private static readonly string[] _componentRequired = ["ComponentType", "ComponentName", "Identity", "MatchedAccount"];
    private static readonly string[] _componentFields = [.. _componentRequired, "State", "Detail"];
    private static readonly string[] _verificationFields = ["ExpectedAccount", "Status", "RunningAsGmsa", "StillFormerAccount"];
    private static readonly string[] _notReachedFields = ["serverName", "reason"];

    [GeneratedRegex(@"^(?:[A-Za-z0-9][A-Za-z0-9_.-]{0,14}\\)?[A-Za-z0-9_][A-Za-z0-9_.-]{0,63}\$?$", RegexOptions.CultureInvariant, 100)]
    private static partial Regex AccountPattern();

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9.-]{0,252}$", RegexOptions.CultureInvariant, 100)]
    private static partial Regex ServerPattern();

    [GeneratedRegex(@"(?:Z|[+-]\d{2}:\d{2})$", RegexOptions.CultureInvariant, 100)]
    private static partial Regex OffsetSuffix();

    [GeneratedRegex(@"(?:password|passwd|pwd|parola|sifre)\s*[=:]\s*\S", RegexOptions.CultureInvariant, 100)]
    private static partial Regex SecretAssignment();

    /// <summary>Validates and reads an upload; throws <see cref="UsageScanFileException"/> with a stable code on any doubt.</summary>
    public static ParsedUsageScan Parse(byte[] bytes, int maxBytes, DateTimeOffset now)
    {
        if (bytes.Length == 0)
        {
            throw new UsageScanFileException(UsageScanFileCodes.Empty);
        }

        if (bytes.Length > maxBytes)
        {
            throw new UsageScanFileException(UsageScanFileCodes.TooLarge);
        }

        using JsonDocument document = Read(bytes);
        JsonElement root = document.RootElement;
        Guard(root, 0);
        return Bundle(root, now);
    }

    private static JsonDocument Read(byte[] bytes)
    {
        ReadOnlySpan<byte> span = bytes;
        if (span.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
        {
            span = span[3..];
        }

        string text;
        try
        {
            text = new UTF8Encoding(false, true).GetString(span);
        }
        catch (DecoderFallbackException)
        {
            throw new UsageScanFileException(UsageScanFileCodes.Encoding);
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new UsageScanFileException(UsageScanFileCodes.Empty);
        }

        if (text.Contains('\0', StringComparison.Ordinal))
        {
            throw new UsageScanFileException(UsageScanFileCodes.Encoding);
        }

        try
        {
            return JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = _maxDepth, AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow });
        }
        catch (JsonException)
        {
            throw new UsageScanFileException(UsageScanFileCodes.Json);
        }
    }

    /// <summary>Whole-document pass: duplicate names and secret-like names or values anywhere, including unknown places.</summary>
    private static void Guard(JsonElement element, int depth)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    if (LooksSecret(property.Name))
                    {
                        throw new UsageScanFileException(UsageScanFileCodes.SecretField);
                    }

                    if (!names.Add(property.Name))
                    {
                        throw new UsageScanFileException(UsageScanFileCodes.DuplicateField);
                    }

                    Guard(property.Value, depth + 1);
                }

                break;
            case JsonValueKind.Array:
                foreach (JsonElement item in element.EnumerateArray())
                {
                    Guard(item, depth + 1);
                }

                break;
            case JsonValueKind.String when SecretAssignment().IsMatch(Fold(element.GetString()!)):
                throw new UsageScanFileException(UsageScanFileCodes.SecretValue);
        }
    }

    /// <summary>Lower-case, accents and Turkish letters folded, separators removed; then any secret word as a substring.</summary>
    internal static bool LooksSecret(string name)
    {
        string key = new([.. Fold(name).Where(c => c is >= 'a' and <= 'z' or >= '0' and <= '9')]);
        return _secretWords.Any(word => key.Contains(word, StringComparison.Ordinal));
    }

    /// <summary>Invariant lower case without combining marks (Ş→s, İ→i, ı→i), so Turkish spellings meet the same words.</summary>
    private static string Fold(string value)
    {
        StringBuilder folded = new(value.Length);
        foreach (char c in value.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                folded.Append(c == 'ı' ? 'i' : char.ToLowerInvariant(c));
            }
        }

        return folded.ToString();
    }

    private static ParsedUsageScan Bundle(JsonElement root, DateTimeOffset now)
    {
        Fields(root, _bundleFields, _bundleFields);
        Const(root, "schema", _bundleSchema);
        DateTimeOffset combinedAt = Date(root.GetProperty("generatedAt"), now);
        string tool = Enum(root.GetProperty("tool"), "Combined", "Jea");
        string[] accounts = [.. Array(root.GetProperty("accounts"), 1, 20).Select(a => Pattern(a, AccountPattern(), 100))];
        if (accounts.Distinct(StringComparer.OrdinalIgnoreCase).Count() != accounts.Length)
        {
            throw new UsageScanFileException(UsageScanFileCodes.Schema);
        }

        JsonElement expectedElement = root.GetProperty("expectedAccount");
        string? expected = expectedElement.ValueKind == JsonValueKind.Null ? null : Pattern(expectedElement, AccountPattern(), 100);
        if (expected is not null && (!expected.EndsWith('$') || accounts.Contains(expected, StringComparer.OrdinalIgnoreCase)))
        {
            throw new UsageScanFileException(UsageScanFileCodes.Schema);
        }

        string[] planned = [.. Array(root.GetProperty("plannedServers"), 1, MaxServers).Select(s => Pattern(s, ServerPattern(), 253))];
        Dictionary<string, string> plannedKeys = new(StringComparer.OrdinalIgnoreCase);
        foreach (string server in planned)
        {
            if (!plannedKeys.TryAdd(server, server))
            {
                throw new UsageScanFileException(UsageScanFileCodes.Servers);
            }
        }

        Dictionary<string, ParsedScanServer> servers = new(StringComparer.OrdinalIgnoreCase);
        List<ParsedScanItem> items = [];
        foreach (JsonElement result in Array(root.GetProperty("results"), 0, MaxServers))
        {
            (ParsedScanServer server, List<ParsedScanItem> found) = Document(result, accounts, expected, combinedAt, now, plannedKeys);
            if (!servers.TryAdd(server.ServerName, server))
            {
                throw new UsageScanFileException(UsageScanFileCodes.Servers);
            }

            items.AddRange(found);
            if (items.Count > MaxItems)
            {
                throw new UsageScanFileException(UsageScanFileCodes.Limits);
            }
        }

        foreach (JsonElement missing in Array(root.GetProperty("notReached"), 0, MaxServers))
        {
            Fields(missing, _notReachedFields, _notReachedFields);
            string name = Pattern(missing.GetProperty("serverName"), ServerPattern(), 253);
            ScanServerResult reason = Enum(missing.GetProperty("reason"), "Unreachable", "NoResult") == "Unreachable"
                ? ScanServerResult.Unreachable : ScanServerResult.NoResult;
            if (!plannedKeys.TryGetValue(name, out string? plannedName)
                || !servers.TryAdd(plannedName, new ParsedScanServer(plannedName, reason, null, null, null, null, null)))
            {
                throw new UsageScanFileException(UsageScanFileCodes.Servers);
            }
        }

        if (servers.Count != planned.Length)
        {
            throw new UsageScanFileException(UsageScanFileCodes.Servers);
        }

        return new ParsedUsageScan(tool, combinedAt, accounts, expected, [.. planned.Select(p => servers[p])], items);
    }

    private static (ParsedScanServer Server, List<ParsedScanItem> Items) Document(JsonElement document, string[] accounts, string? expected,
        DateTimeOffset combinedAt, DateTimeOffset now, Dictionary<string, string> plannedKeys)
    {
        Fields(document, _documentRequired, _documentFields);
        Const(document, "schema", _documentSchema);
        string reported = Pattern(document.GetProperty("serverName"), ServerPattern(), 253);
        if (!plannedKeys.TryGetValue(reported, out string? serverName))
        {
            throw new UsageScanFileException(UsageScanFileCodes.Servers);
        }

        DateTimeOffset scannedAt = Date(document.GetProperty("generatedAt"), now);
        if (scannedAt > combinedAt + ClockSkew)
        {
            throw new UsageScanFileException(UsageScanFileCodes.Inconsistent);
        }

        if (document.GetProperty("durationMs") is not { ValueKind: JsonValueKind.Number } duration || !duration.TryGetInt64(out long ms) || ms < 0)
        {
            throw new UsageScanFileException(UsageScanFileCodes.Schema);
        }

        string[] searched = [.. Array(document.GetProperty("accounts"), 1, 20).Select(a => Pattern(a, AccountPattern(), 100))];
        if (searched.Length != accounts.Length || !searched.All(a => accounts.Contains(a, StringComparer.OrdinalIgnoreCase)))
        {
            throw new UsageScanFileException(UsageScanFileCodes.Inconsistent);
        }

        string scanResult = Enum(document.GetProperty("scanResult"), "Success", "Partial", "Failed");
        JsonElement sources = document.GetProperty("sources");
        Fields(sources, _sourceFields, _sourceFields);
        string services = Enum(sources.GetProperty("WindowsServices"), "Success", "Failed");
        string tasks = Enum(sources.GetProperty("ScheduledTasks"), "Success", "Failed");
        string iis = Enum(sources.GetProperty("Iis"), "Success", "Failed", "NotInstalled");
        int failed = new[] { services, tasks, iis }.Count(s => s == "Failed");
        string expectedResult = failed == 0 ? "Success" : failed == _sourceFields.Length ? "Failed" : "Partial";
        if (scanResult != expectedResult)
        {
            throw new UsageScanFileException(UsageScanFileCodes.Inconsistent);
        }

        string[] warnings = [.. Array(document.GetProperty("warnings"), 0, MaxWarnings).Select(w => Text(w, 200, required: true)!)];
        List<ParsedScanItem> items = [];
        HashSet<string> formerKeys = new(StringComparer.Ordinal);
        foreach (JsonElement component in Array(document.GetProperty("components"), 0, MaxComponentsPerServer))
        {
            ParsedScanItem item = Component(component, serverName, "Former", accounts);
            if (!formerKeys.Add(Key(item)))
            {
                throw new UsageScanFileException(UsageScanFileCodes.Inconsistent);
            }

            items.Add(item);
        }

        if (scanResult == "Failed" && items.Count > 0)
        {
            throw new UsageScanFileException(UsageScanFileCodes.Inconsistent);
        }

        bool hasVerification = document.TryGetProperty("verification", out JsonElement verification) && verification.ValueKind != JsonValueKind.Null;
        if ((expected is null) == hasVerification)
        {
            throw new UsageScanFileException(UsageScanFileCodes.Inconsistent);
        }

        if (expected is not null)
        {
            items.AddRange(Verification(verification, serverName, expected, formerKeys, accounts));
        }

        return (new ParsedScanServer(serverName, System.Enum.Parse<ScanServerResult>(scanResult), services, tasks, iis, scannedAt,
            warnings.Length == 0 ? null : Truncate(string.Join("; ", warnings), 1000)), items);
    }

    private static IEnumerable<ParsedScanItem> Verification(JsonElement verification, string serverName, string expected, HashSet<string> formerKeys,
        string[] accounts)
    {
        Fields(verification, _verificationFields, _verificationFields);
        if (!string.Equals(Pattern(verification.GetProperty("ExpectedAccount"), AccountPattern(), 100), expected, StringComparison.OrdinalIgnoreCase))
        {
            throw new UsageScanFileException(UsageScanFileCodes.Inconsistent);
        }

        string status = Enum(verification.GetProperty("Status"), "Converted", "NotConverted", "NoComponents");
        List<ParsedScanItem> gmsa = [.. Array(verification.GetProperty("RunningAsGmsa"), 0, MaxComponentsPerServer)
            .Select(c => Component(c, serverName, "Expected", [expected]))];
        HashSet<string> still = [.. Array(verification.GetProperty("StillFormerAccount"), 0, MaxComponentsPerServer)
            .Select(c => Key(Component(c, serverName, "Former", accounts)))];
        string expectedStatus = formerKeys.Count > 0 ? "NotConverted" : gmsa.Count > 0 ? "Converted" : "NoComponents";
        if (!still.SetEquals(formerKeys) || status != expectedStatus || gmsa.Select(Key).Distinct(StringComparer.Ordinal).Count() != gmsa.Count)
        {
            throw new UsageScanFileException(UsageScanFileCodes.Inconsistent);
        }

        return gmsa;
    }

    private static ParsedScanItem Component(JsonElement component, string serverName, string role, string[] allowedAccounts)
    {
        Fields(component, _componentRequired, _componentFields);
        string type = Enum(component.GetProperty("ComponentType"), [.. UsageScanOutcomes.ComponentTypes]);
        string name = Text(component.GetProperty("ComponentName"), 1024, required: true)!;
        string identity = Text(component.GetProperty("Identity"), 256, required: true)!;
        string matched = Pattern(component.GetProperty("MatchedAccount"), AccountPattern(), 100);
        string canonical = allowedAccounts.FirstOrDefault(a => string.Equals(a, matched, StringComparison.OrdinalIgnoreCase))
            ?? throw new UsageScanFileException(UsageScanFileCodes.Inconsistent);
        string? state = component.TryGetProperty("State", out JsonElement s) ? Text(s, 64) : null;
        string? detail = component.TryGetProperty("Detail", out JsonElement d) ? Text(d, 1024) : null;
        return new ParsedScanItem(serverName, role, canonical, type, name, identity, state, detail);
    }

    private static string Key(ParsedScanItem item) => $"{item.ComponentType}|{item.ComponentName}|{item.MatchedAccount.ToUpperInvariant()}";

    /// <summary>Object with exactly the allowed names and every required name.</summary>
    private static void Fields(JsonElement element, string[] required, string[] allowed)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new UsageScanFileException(UsageScanFileCodes.Schema);
        }

        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (!allowed.Contains(property.Name, StringComparer.Ordinal))
            {
                throw new UsageScanFileException(UsageScanFileCodes.Schema);
            }
        }

        if (required.Any(name => !element.TryGetProperty(name, out _)))
        {
            throw new UsageScanFileException(UsageScanFileCodes.Schema);
        }
    }

    private static void Const(JsonElement element, string name, string value)
    {
        if (element.GetProperty(name) is not { ValueKind: JsonValueKind.String } text || text.GetString() != value)
        {
            throw new UsageScanFileException(UsageScanFileCodes.Schema);
        }
    }

    private static IEnumerable<JsonElement> Array(JsonElement element, int min, int max)
    {
        if (element.ValueKind != JsonValueKind.Array)
        {
            throw new UsageScanFileException(UsageScanFileCodes.Schema);
        }

        int count = element.GetArrayLength();
        if (count < min)
        {
            throw new UsageScanFileException(UsageScanFileCodes.Schema);
        }

        return count > max ? throw new UsageScanFileException(UsageScanFileCodes.Limits) : element.EnumerateArray();
    }

    private static string Enum(JsonElement element, params string[] values) =>
        element.ValueKind == JsonValueKind.String && values.Contains(element.GetString(), StringComparer.Ordinal)
            ? element.GetString()! : throw new UsageScanFileException(UsageScanFileCodes.Schema);

    private static string Pattern(JsonElement element, Regex pattern, int max) =>
        element.ValueKind == JsonValueKind.String && element.GetString() is { } value && value.Length <= max && pattern.IsMatch(value)
            ? value : throw new UsageScanFileException(UsageScanFileCodes.Schema);

    /// <summary>Bounded text without control characters; null allowed unless required.</summary>
    private static string? Text(JsonElement element, int max, bool required = false)
    {
        if (element.ValueKind == JsonValueKind.Null && !required)
        {
            return null;
        }

        return element.ValueKind == JsonValueKind.String && element.GetString() is { Length: > 0 } value && value.Length <= max && !value.Any(char.IsControl)
            ? value : throw new UsageScanFileException(UsageScanFileCodes.Schema);
    }

    /// <summary>ISO 8601 with an explicit offset; never in the future beyond the clock skew.</summary>
    private static DateTimeOffset Date(JsonElement element, DateTimeOffset now)
    {
        if (element.ValueKind != JsonValueKind.String || element.GetString() is not { Length: <= 40 } text || !OffsetSuffix().IsMatch(text)
            || !DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset value))
        {
            throw new UsageScanFileException(UsageScanFileCodes.Schema);
        }

        return value > now + ClockSkew ? throw new UsageScanFileException(UsageScanFileCodes.FutureDate) : value;
    }

    private static string Truncate(string value, int length) => value.Length <= length ? value : value[..length];
}
