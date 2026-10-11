namespace SecureOps.Infrastructure.ItsmRecords;

/// <summary>
/// Strict allow-list for record numbers: the kind's prefix (<c>OR-</c> or <c>OCO-</c>, any case) followed by
/// 1-20 ASCII digits, after trimming outer whitespace. Anything else is rejected, so a number can be placed in a
/// source filter without escaping. Callers may pre-validate with <see cref="Normalize"/>.
/// </summary>
public static class ItsmRecordNumber
{
    /// <summary>Maximum digit count after the prefix.</summary>
    public const int MaxDigits = 20;

    private const int _maxInputLength = 64;

    /// <summary>Returns the canonical upper-case number, or null when the value is not a valid number of this kind.</summary>
    public static string? Normalize(ItsmRecordKind kind, string? value)
    {
        if (value is null || value.Length > _maxInputLength || !Enum.IsDefined(kind))
        {
            return null;
        }

        string prefix = Prefix(kind);
        string trimmed = value.Trim();
        if (!trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        ReadOnlySpan<char> digits = trimmed.AsSpan(prefix.Length);
        if (digits.Length is 0 or > MaxDigits)
        {
            return null;
        }

        foreach (char digit in digits)
        {
            if (!char.IsAsciiDigit(digit))
            {
                return null;
            }
        }

        return string.Concat(prefix, digits);
    }

    /// <summary>Canonical prefix for the kind.</summary>
    public static string Prefix(ItsmRecordKind kind) => kind switch
    {
        ItsmRecordKind.Or => "OR-",
        ItsmRecordKind.Oco => "OCO-",
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
}
