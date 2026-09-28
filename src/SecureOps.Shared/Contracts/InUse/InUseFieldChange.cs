namespace SecureOps.Shared.Contracts.InUse;

/// <summary>Literal before/after evidence with independent review or execution scope.</summary>
public sealed record InUseFieldChange(string Category, string Field, string? Before, string? After);

/// <summary>Trusted human-facing labels. Stable identifiers belong in technical evidence only.</summary>
public static class InUsePersonLabel
{
    /// <summary>Never treats a SID or GUID as a display name or substitutes another actor.</summary>
    public static string Format(string? display, string? account)
    {
        display = Human(display);
        account = Human(account);
        return display is null ? account ?? "Kullanıcı adı çözümlenemedi"
            : account is null || string.Equals(display, account, StringComparison.OrdinalIgnoreCase) ? display
            : $"{display} ({account})";
    }

    private static string? Human(string? value) => string.IsNullOrWhiteSpace(value)
        || Guid.TryParse(value, out _) || value.StartsWith("S-1-", StringComparison.OrdinalIgnoreCase)
        ? null : value.Trim();
}
