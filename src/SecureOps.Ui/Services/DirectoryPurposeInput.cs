namespace SecureOps.Ui.Services;

/// <summary>
/// Client-side rules for the optional purpose carried by read-only Directory Explorer queries.
/// </summary>
/// <remarks>
/// <para>
/// Mirrors <c>DirectoryLookupPurpose</c> on the server: blank means no purpose, a supplied value is
/// trimmed, bounded by <see cref="MaxLength"/>, and rejected when it contains control characters.
/// The server re-validates everything — this exists so an operator sees the problem next to the
/// field instead of as an error panel after a round-trip.
/// </para>
/// <para>
/// Read-only directory queries only. Write workflows keep their own required reason: an access
/// decision or a session revocation is an action taken against someone, and the justification is
/// what makes it defensible afterwards. Looking something up is not.
/// </para>
/// </remarks>
public static class DirectoryPurposeInput
{
    /// <summary>Longest purpose the directory endpoints accept.</summary>
    /// <remarks>Must stay equal to <c>DirectoryExplorerOptions.MaxPurposeLength</c>.</remarks>
    public const int MaxLength = 256;

    /// <summary>
    /// Prepares a purpose for transmission.
    /// </summary>
    /// <param name="purpose">Raw field value.</param>
    /// <returns>Trimmed text, or <c>null</c> when the operator supplied nothing meaningful.</returns>
    /// <remarks>
    /// Whitespace collapses to <c>null</c> rather than to an empty string. The two are equivalent to
    /// the server, but sending <c>null</c> states plainly that no purpose was given instead of
    /// recording an empty one that reads like a purpose nobody could see.
    /// </remarks>
    public static string? Normalize(string? purpose) =>
        string.IsNullOrWhiteSpace(purpose) ? null : purpose.Trim();

    /// <summary>
    /// Validates a purpose the operator typed.
    /// </summary>
    /// <param name="purpose">Raw field value.</param>
    /// <returns>Operator-facing message, or <c>null</c> when the value is acceptable.</returns>
    /// <remarks>
    /// Length is measured after trimming, matching the server. Trailing spaces should not be able to
    /// push an otherwise valid sentence over the limit.
    /// </remarks>
    public static string? Validate(string? purpose)
    {
        string? normalized = Normalize(purpose);

        if (normalized is null)
        {
            return null;
        }

        if (normalized.Length > MaxLength)
        {
            return $"Açıklama en fazla {MaxLength} karakter olabilir.";
        }

        // Rejected before the request rather than after: the server refuses control characters, and
        // they usually arrive by pasting from a terminal or a spreadsheet cell.
        return normalized.Any(char.IsControl)
            ? "Açıklama yazdırılamayan karakter içeremez."
            : null;
    }
}
