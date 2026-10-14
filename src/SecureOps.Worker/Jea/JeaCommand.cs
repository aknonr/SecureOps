using System.Text.RegularExpressions;

namespace SecureOps.Worker.Jea;

/// <summary>
/// The only commands the Worker can send to a managed server (AGENTS.md rules 1 and 3). Each entry names one registered JEA session
/// configuration, its one visible read-only function and that function's typed parameters with their value rules. There is no way to
/// add a command at run time, to pass script text or to reach a default endpoint: <see cref="JeaCommand"/> can only be built from this
/// catalog. Adding an entry is a reviewed source change (and, for a new endpoint, an ADR).
/// </summary>
public static class JeaEndpointCatalog
{
    /// <summary>Account name as the usage endpoint accepts it (<c>service-account-usage-scan-v1</c> pattern, no quotes, spaces or operators).</summary>
    internal const string AccountPattern = @"^(?:[A-Za-z0-9][A-Za-z0-9_.-]{0,14}\\)?[A-Za-z0-9_][A-Za-z0-9_.-]{0,63}\$?\z";

    /// <summary>Host name or FQDN accepted as a connection target (letters, digits, hyphen, dots; 1–255).</summary>
    internal const string ComputerNamePattern = @"^(?=.{1,255}\z)[A-Za-z0-9](?:[A-Za-z0-9-]{0,62})(?:\.[A-Za-z0-9](?:[A-Za-z0-9-]{0,62}))*\z";

    /// <summary>Verbs a catalog function may carry: read-only verbs only (docs/agent-guides/040).</summary>
    internal static readonly IReadOnlySet<string> ReadOnlyVerbs = new HashSet<string>(["Get", "Test", "Measure"], StringComparer.Ordinal);

    /// <summary>Service account usage discovery (ADR-0024 R2): <c>Get-SecureOpsAccountUsage</c> on <c>SecureOpsServiceAccountUsage</c>.</summary>
    public static JeaEndpoint ServiceAccountUsage { get; } = new("SecureOpsServiceAccountUsage", "Get-SecureOpsAccountUsage",
    [
        new JeaParameterSpec("Account", JeaParameterKind.StringArray, true, AccountPattern, 1, 20),
        new JeaParameterSpec("ExpectedAccount", JeaParameterKind.String, false, @"^(?:[A-Za-z0-9][A-Za-z0-9_.-]{0,14}\\)?[A-Za-z0-9_][A-Za-z0-9_.-]{0,63}\$\z", 1, 1),
        new JeaParameterSpec("AsJson", JeaParameterKind.Switch, true, null, 0, 0)
    ]);

    /// <summary>Every allow-listed endpoint.</summary>
    public static IReadOnlyList<JeaEndpoint> All { get; } = [ServiceAccountUsage];
}

/// <summary>One registered JEA session configuration and its one visible function.</summary>
/// <param name="ConfigurationName">Session configuration name passed as <c>-ConfigurationName</c>; never a default (<c>Microsoft.*</c>) endpoint.</param>
/// <param name="FunctionName">The single visible function the runner may call.</param>
/// <param name="Parameters">Its typed parameters.</param>
public sealed record JeaEndpoint(string ConfigurationName, string FunctionName, IReadOnlyList<JeaParameterSpec> Parameters);

/// <summary>Kind of a catalog parameter value.</summary>
public enum JeaParameterKind
{
    /// <summary>One string matching the pattern.</summary>
    String,
    /// <summary>A string array (count bounded), each item matching the pattern.</summary>
    StringArray,
    /// <summary>A switch; present or absent, no value.</summary>
    Switch
}

/// <summary>A typed, validated parameter of a catalog function.</summary>
/// <param name="Name">PowerShell parameter name.</param>
/// <param name="Kind">Value kind.</param>
/// <param name="Required">Whether the command must carry it.</param>
/// <param name="Pattern">Full-match pattern for every string value (null only for a switch).</param>
/// <param name="MinCount">Fewest array items.</param>
/// <param name="MaxCount">Most array items.</param>
public sealed record JeaParameterSpec(string Name, JeaParameterKind Kind, bool Required, string? Pattern, int MinCount, int MaxCount);

/// <summary>Thrown when a command does not match the catalog; nothing is sent anywhere.</summary>
public sealed class JeaCommandRejectedException(string code) : Exception("JeaCommandRejected:" + code)
{
    /// <summary>Stable reason (<c>unknownEndpoint</c>, <c>unknownFunction</c>, <c>unknownParameter</c>, <c>missingParameter</c>, <c>invalidValue</c>, <c>defaultEndpoint</c>).</summary>
    public string Code { get; } = code;
}

/// <summary>
/// One allow-listed invocation: catalog endpoint + function + typed, validated parameter values. Immutable; the constructor is private,
/// so every command that reaches a runner passed <see cref="Create"/>.
/// </summary>
public sealed class JeaCommand
{
    private static readonly TimeSpan _patternTimeout = TimeSpan.FromMilliseconds(100);

    private JeaCommand(JeaEndpoint endpoint, IReadOnlyList<KeyValuePair<string, object>> parameters)
    {
        Endpoint = endpoint;
        Parameters = parameters;
    }

    /// <summary>The endpoint (configuration and function).</summary>
    public JeaEndpoint Endpoint { get; }

    /// <summary>Parameter name → typed value (<see cref="string"/>, <see cref="string"/>[] or <c>true</c> for a present switch), in catalog order.</summary>
    public IReadOnlyList<KeyValuePair<string, object>> Parameters { get; }

    /// <summary>Validates <paramref name="values"/> against the catalog entry for <paramref name="configurationName"/>/<paramref name="functionName"/>.</summary>
    /// <exception cref="JeaCommandRejectedException">Anything outside the catalog or its value rules.</exception>
    public static JeaCommand Create(string configurationName, string functionName, IReadOnlyDictionary<string, object?> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (string.IsNullOrWhiteSpace(configurationName) || configurationName.StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase))
        {
            throw new JeaCommandRejectedException("defaultEndpoint");
        }

        JeaEndpoint endpoint = JeaEndpointCatalog.All.FirstOrDefault(e => string.Equals(e.ConfigurationName, configurationName, StringComparison.Ordinal))
            ?? throw new JeaCommandRejectedException("unknownEndpoint");
        if (!string.Equals(endpoint.FunctionName, functionName, StringComparison.Ordinal))
        {
            throw new JeaCommandRejectedException("unknownFunction");
        }

        foreach (string name in values.Keys)
        {
            if (!endpoint.Parameters.Any(p => string.Equals(p.Name, name, StringComparison.Ordinal)))
            {
                throw new JeaCommandRejectedException("unknownParameter");
            }
        }

        List<KeyValuePair<string, object>> parameters = [];
        foreach (JeaParameterSpec spec in endpoint.Parameters)
        {
            if (!values.TryGetValue(spec.Name, out object? value) || value is null || (spec.Kind == JeaParameterKind.Switch && value is false))
            {
                if (spec.Required)
                {
                    throw new JeaCommandRejectedException("missingParameter");
                }

                continue;
            }

            parameters.Add(new(spec.Name, Validate(spec, value)));
        }

        return new JeaCommand(endpoint, parameters);
    }

    /// <summary>Whether <paramref name="computerName"/> is a plain host name or FQDN (the only connection targets accepted).</summary>
    public static bool IsComputerName(string? computerName) =>
        computerName is { Length: > 0 } && Regex.IsMatch(computerName, JeaEndpointCatalog.ComputerNamePattern, RegexOptions.CultureInvariant, _patternTimeout);

    private static object Validate(JeaParameterSpec spec, object value)
    {
        switch (spec.Kind)
        {
            case JeaParameterKind.Switch:
                return value is true ? true : throw new JeaCommandRejectedException("invalidValue");
            case JeaParameterKind.String:
                return value is string text && Matches(spec, text) ? text : throw new JeaCommandRejectedException("invalidValue");
            case JeaParameterKind.StringArray:
                if (value is not IEnumerable<string> items)
                {
                    throw new JeaCommandRejectedException("invalidValue");
                }

                string[] array = [.. items];
                return array.Length >= spec.MinCount && array.Length <= spec.MaxCount && array.All(item => Matches(spec, item))
                    ? array : throw new JeaCommandRejectedException("invalidValue");
            default:
                throw new JeaCommandRejectedException("invalidValue");
        }
    }

    private static bool Matches(JeaParameterSpec spec, string? text) =>
        text is not null && spec.Pattern is not null && Regex.IsMatch(text, spec.Pattern, RegexOptions.CultureInvariant, _patternTimeout);
}
