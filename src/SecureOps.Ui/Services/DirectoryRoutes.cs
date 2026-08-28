namespace SecureOps.Ui.Services;

/// <summary>
/// Builds the addressable directory URLs that navigation between accounts and groups uses.
/// </summary>
/// <remarks>
/// Directory navigation used to happen entirely inside component state: clicking a group replaced
/// what was on screen and left no trace in the browser. Going from a user, into their group
/// memberships, into a group, and pressing Back threw away the original account and the tab it was
/// found on — the operator had to retype the lookup.
/// <para>
/// Putting the target in the URL fixes that at the root. Back and Forward work because each step is
/// a real history entry, and because these are real links the operator also gets Ctrl+Click,
/// middle-click, and "Yeni sekmede aç" without the UI implementing any of it.
/// </para>
/// <para>
/// Only exact, already-public identifiers go in a URL: an account name or a server-supplied group
/// lookup key. Nothing here carries a session handle, a token, a correlation value, or a purpose
/// statement.
/// </para>
/// </remarks>
public static class DirectoryRoutes
{
    /// <summary>Account lookup page.</summary>
    public const string UsersPath = "directory/users";

    /// <summary>Group analysis page.</summary>
    public const string GroupsPath = "directory/groups";

    /// <summary>
    /// Link to one group's analysis.
    /// </summary>
    /// <param name="lookupKey">Exact group identifier, normally the server-returned lookup key.</param>
    /// <returns>A relative URL, or the bare page when no exact identifier is available.</returns>
    public static string Group(string? lookupKey) =>
        string.IsNullOrWhiteSpace(lookupKey)
            ? GroupsPath
            : $"{GroupsPath}?group={Uri.EscapeDataString(lookupKey.Trim())}";

    /// <summary>
    /// Link to one account, optionally opening a specific tab.
    /// </summary>
    /// <param name="account">Exact account identifier.</param>
    /// <param name="tab">Tab to open, or <see langword="null"/> for the default.</param>
    /// <returns>A relative URL, or the bare page when no exact identifier is available.</returns>
    /// <remarks>
    /// The tab belongs in the URL for the same reason the account does: an operator who came back
    /// from a group expects to land on the membership list they left, not on the summary.
    /// </remarks>
    public static string User(string? account, string? tab = null)
    {
        if (string.IsNullOrWhiteSpace(account))
        {
            return UsersPath;
        }

        string url = $"{UsersPath}?account={Uri.EscapeDataString(account.Trim())}";

        return string.IsNullOrWhiteSpace(tab) ? url : $"{url}&tab={Uri.EscapeDataString(tab)}";
    }
}
