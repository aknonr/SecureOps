namespace SecureOps.Ui.Services;

/// <summary>
/// The browser-authenticated principal, as far as the UI shell can describe it.
/// </summary>
/// <remarks>
/// This carries only what the authentication cookie proves: who signed in and how. It deliberately
/// carries no roles or permissions — those come from <c>GET /api/v1/access/me</c>, which the API
/// resolves independently of the authentication source. Keeping them apart means the UI cannot drift
/// into showing an authorization state the server does not agree with.
/// <para>
/// <see cref="DisplayName"/> falls back to <see cref="AccountName"/> until an identity provider
/// supplies a full name. Directory attributes such as e-mail, department, and title are not available
/// from any current endpoint; see <c>docs/26-ui-backend-contract-gaps.md</c>.
/// </para>
/// </remarks>
/// <param name="AccountName">Account identifier the user signed in with.</param>
/// <param name="DisplayName">Full name when known, otherwise the account name.</param>
/// <param name="AuthenticationSource">How the session was established.</param>
public sealed record SignedInUser(
    string AccountName,
    string DisplayName,
    string AuthenticationSource)
{
    /// <summary>
    /// Up to two initials for a compact avatar.
    /// </summary>
    public string Initials
    {
        get
        {
            string[] parts = DisplayName
                .Split([' ', '.', '_', '-'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            return parts.Length switch
            {
                0 => "?",
                1 => parts[0][..1].ToUpperInvariant(),
                _ => string.Concat(parts[0][..1], parts[^1][..1]).ToUpperInvariant()
            };
        }
    }
}
