namespace SecureOps.Ui.Services;

/// <summary>
/// Outcome of client-side account validation.
/// </summary>
/// <param name="IsValid">Whether the value passes the mirrored rules.</param>
/// <param name="NormalizedPreview">Value as the server is expected to normalize it.</param>
/// <param name="Message">Operator-facing rejection reason when invalid.</param>
/// <param name="Advisory">Non-blocking note shown alongside a valid value.</param>
public sealed record AccountValidationResult(
    bool IsValid,
    string? NormalizedPreview,
    string? Message,
    string? Advisory);

/// <summary>
/// Client-side mirror of the server's exact-account rules, used to catch obviously invalid input
/// before it costs a round-trip, a rate-limit slot, and an audit entry.
/// </summary>
/// <remarks>
/// This mirrors <c>IdentityAccountNormalizer</c> and <c>IdentityProviderInputGuard</c> in
/// <c>SecureOps.Infrastructure.Identity</c>. It is deliberately no stricter than the server:
/// <list type="bullet">
///   <item><description><c>DOMAIN\account</c> is accepted; the prefix is stripped, as the server does.</description></item>
///   <item><description>UPN-shaped values are accepted because <c>@</c> is inside the server allow-list.</description></item>
///   <item><description>Only genuinely unusable input is blocked: multiple accounts, wildcard and LDAP
///   filter characters, and characters outside the allow-list.</description></item>
/// </list>
/// <para>
/// The server remains authoritative. The maximum length is supplied from the live
/// capabilities endpoint; the allow-list below is a UI constant because the capabilities contract does
/// not expose <c>AllowedAccountPattern</c> (see <c>docs/26-ui-backend-contract-gaps.md</c>). If the
/// server's pattern is ever narrowed, the server rejects the value and the UI shows its message —
/// the mirror failing open is the safe direction.
/// </para>
/// </remarks>
public static class AccountInputRules
{
    /// <summary>
    /// Default maximum account length, used until the capabilities endpoint answers.
    /// </summary>
    public const int DefaultMaxAccountLength = 128;

    /// <summary>
    /// Characters the server rejects as wildcard or LDAP filter syntax.
    /// </summary>
    private static readonly char[] _searchPatternCharacters =
        ['*', '%', '?', '[', ']', '(', ')', '|', '&', '=', '<', '>', '"'];

    /// <summary>
    /// Validates one account value against the mirrored server rules.
    /// </summary>
    /// <param name="account">Raw operator input.</param>
    /// <param name="maxAccountLength">Server-declared maximum length after normalization.</param>
    /// <param name="supportsUpnLookup">Whether the server has UPN-shaped lookup enabled.</param>
    /// <returns>Validation outcome.</returns>
    public static AccountValidationResult Validate(
        string? account,
        int maxAccountLength = DefaultMaxAccountLength,
        bool supportsUpnLookup = true)
    {
        if (string.IsNullOrWhiteSpace(account))
        {
            return Invalid("Hesap zorunludur.");
        }

        string value = account.Trim();

        // Checked before prefix stripping so "a b" is reported as "more than one account" rather than
        // as an allow-list failure, which matches how the server classifies it.
        if (ContainsBulkSeparator(value))
        {
            return Invalid("Tek seferde yalnızca bir hesap sorgulanabilir. Boşluk, virgül veya noktalı virgül kullanmayın.");
        }

        string normalized = StripDomainPrefix(value);

        if (string.IsNullOrWhiteSpace(normalized))
        {
            return Invalid("Alan adı ayıklandıktan sonra geriye hesap adı kalmıyor.");
        }

        if (normalized.Length > maxAccountLength)
        {
            return Invalid($"Hesap adı en fazla {maxAccountLength} karakter olabilir.");
        }

        if (normalized.IndexOfAny(_searchPatternCharacters) >= 0)
        {
            return Invalid("Joker karakter veya arama ifadesi kullanılamaz. Tam hesap adını girin.");
        }

        if (!normalized.All(IsAllowedCharacter))
        {
            return Invalid("Hesap adı yalnızca harf, rakam ve . _ - @ karakterlerini içerebilir.");
        }

        string? advisory = null;

        if (!supportsUpnLookup && normalized.Contains('@', StringComparison.Ordinal))
        {
            advisory = "Bu ortamda UPN (e-posta biçimli) sorgu kapalı. Sonuç bulunamazsa hesap adını kullanın.";
        }
        else if (!string.Equals(normalized, value, StringComparison.Ordinal))
        {
            advisory = $"Sorgu şu hesap için çalışacak: {normalized}";
        }

        return new AccountValidationResult(true, normalized, null, advisory);
    }

    /// <summary>
    /// Removes a <c>DOMAIN\</c> prefix the way the server's normalizer does.
    /// </summary>
    /// <param name="value">Trimmed account value.</param>
    /// <returns>Account without a domain prefix.</returns>
    public static string StripDomainPrefix(string value)
    {
        int lastSlash = value.LastIndexOf('\\');
        return lastSlash >= 0 ? value[(lastSlash + 1)..] : value;
    }

    private static bool ContainsBulkSeparator(string value) =>
        value.Any(char.IsWhiteSpace)
        || value.Contains(',', StringComparison.Ordinal)
        || value.Contains(';', StringComparison.Ordinal);

    // Mirrors the configured allow-list "^[a-zA-Z0-9._@-]+$" as a character predicate. A predicate
    // avoids running operator-supplied text through a regex engine in the UI process entirely.
    private static bool IsAllowedCharacter(char candidate) =>
        candidate is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9')
            or '.' or '_' or '-' or '@';

    private static AccountValidationResult Invalid(string message) =>
        new(false, null, message, null);
}
