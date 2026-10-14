using SecureOps.Shared.Configuration;

namespace SecureOps.Infrastructure.OperationalRecords;

/// <summary>Validates Jira issue keys and derives operator links only from server configuration.</summary>
public static class JiraIssueLink
{
    /// <summary>Whether a value has the strict <c>PROJECT-123</c> issue-key shape.</summary>
    public static bool IsValidKey(string? issueKey)
    {
        if (string.IsNullOrWhiteSpace(issueKey) || issueKey.Length > 64)
        {
            return false;
        }

        int separator = issueKey.LastIndexOf('-');
        return separator > 0
            && separator < issueKey.Length - 1
            && issueKey[..separator].All(character => char.IsAsciiLetterUpper(character) || char.IsAsciiDigit(character))
            && char.IsAsciiLetterUpper(issueKey[0])
            && issueKey[(separator + 1)..].All(char.IsAsciiDigit);
    }

    /// <summary>
    /// Returns the configured browse address followed by the key, or null when the provider is not
    /// Corporate, the address is absent or not a plain absolute HTTPS URL, or the key is invalid.
    /// </summary>
    /// <remarks>Simulation and fake keys are never linked: they do not exist in any Jira.</remarks>
    public static string? Create(JiraIntegrationOptions options, string? issueKey)
    {
        if (!string.Equals(options.Provider, "Corporate", StringComparison.OrdinalIgnoreCase)
            || !IsValidKey(issueKey)
            || !Uri.TryCreate(options.IssueBrowseBaseUrl, UriKind.Absolute, out Uri? baseUri)
            || baseUri.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(baseUri.UserInfo)
            || !string.IsNullOrEmpty(baseUri.Query)
            || !string.IsNullOrEmpty(baseUri.Fragment))
        {
            return null;
        }

        string root = baseUri.AbsoluteUri;
        return (root.EndsWith('/') ? root : root + "/") + issueKey;
    }
}
