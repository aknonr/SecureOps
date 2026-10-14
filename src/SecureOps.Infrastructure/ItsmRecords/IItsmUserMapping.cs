namespace SecureOps.Infrastructure.ItsmRecords;

/// <summary>
/// Exact, read-only mapping of one product user to the ITSM user id (ADR-0032 and its 2026-10-13 amendment). The
/// caller passes only values the server holds for the signed-in user: the registration number of the product user
/// record and the user name derived from the server-verified session identity. There is no free-text or person
/// search; each lookup is a single exact equality on one source column. Zero or several matches, a conflict between
/// the two keys, or any failure is not a mapping, and the consuming flow must stop.
/// </summary>
public interface IItsmUserMapping
{
    /// <summary>
    /// Maps an exact, case-sensitive user name (the source compares <c>m_username</c> case-sensitively; ADR-0032
    /// amendment 2026-10-14). An invalid name returns Unknown/InvalidIdentifier without a remote call.
    /// Caller cancellation propagates as <see cref="OperationCanceledException"/>; a dependency timeout is Unknown.
    /// </summary>
    public Task<ItsmUserMatchResult> MatchByUserNameAsync(string? userName, CancellationToken cancellationToken);

    /// <summary>
    /// Maps an exact registration number in the source's canonical form (see <see cref="ItsmRegistrationNumber"/>). An invalid
    /// number returns Unknown/InvalidIdentifier without a remote call. The number is a query key only: it is never
    /// returned, logged or recorded in telemetry. Cancellation and timeouts behave as for the user name.
    /// </summary>
    public Task<ItsmUserMatchResult> MatchByRegistrationNumberAsync(string? registrationNumber, CancellationToken cancellationToken);

    /// <summary>
    /// Maps a product user by registration number first and user name second (ADR-0032 amendment 2026-10-13): when
    /// both are given they must not point at different users (a conflict is Ambiguous), an Unknown registration result
    /// never falls back to the user name, and only a NotMatched registration result tries the user name alone. Null or
    /// blank values count as not given; when neither is given the result is Unknown/InvalidIdentifier without a remote
    /// call.
    /// </summary>
    public Task<ItsmUserMatchResult> MatchAsync(string? registrationNumber, string? userName, CancellationToken cancellationToken);
}

/// <summary>What the source proved about the requested user name.</summary>
public enum ItsmUserMatchStatus
{
    /// <summary>Exactly one user has this exact user name.</summary>
    Matched,
    /// <summary>A complete, successful response held no user with this user name. The consuming flow stops.</summary>
    NotMatched,
    /// <summary>More than one user matched; none is chosen. The consuming flow stops.</summary>
    Ambiguous,
    /// <summary>The source could not prove an answer; see <see cref="ItsmUserMatchResult.Reason"/>. Never means "absent".</summary>
    Unknown
}

/// <summary>
/// The ITSM identifiers of a matched user. No name, e-mail or phone number is carried (a phone number would need
/// its own field permission under ADR-0032).
/// </summary>
/// <param name="UserId">ITSM user id.</param>
/// <param name="OrganizationId">Raw organization (OBS) id; null when the source left it empty.</param>
/// <param name="HomeLocationId">Raw home location id; null when the source left it empty.</param>
public sealed record ItsmUserReference(string UserId, string? OrganizationId, string? HomeLocationId);

/// <summary>Outcome of one user mapping. The requested user name and registration number are deliberately not echoed.</summary>
/// <param name="Status">What the source proved.</param>
/// <param name="Reason">Why the result is Unknown; None otherwise.</param>
/// <param name="User">The single matching user when Matched.</param>
/// <param name="Provider">Answering provider (Disabled, Fake or TuruncuHat); Fake results are synthetic evidence.</param>
public sealed record ItsmUserMatchResult(
    ItsmUserMatchStatus Status,
    ItsmRecordLookupReason Reason,
    ItsmUserReference? User,
    string Provider)
{
    /// <summary>Exactly one user matched.</summary>
    public static ItsmUserMatchResult Matched(ItsmUserReference user, string provider) =>
        new(ItsmUserMatchStatus.Matched, ItsmRecordLookupReason.None, user, provider);

    /// <summary>A complete response held no user with this name.</summary>
    public static ItsmUserMatchResult NotMatched(string provider) =>
        new(ItsmUserMatchStatus.NotMatched, ItsmRecordLookupReason.None, null, provider);

    /// <summary>More than one user matched.</summary>
    public static ItsmUserMatchResult Ambiguous(string provider) =>
        new(ItsmUserMatchStatus.Ambiguous, ItsmRecordLookupReason.None, null, provider);

    /// <summary>The source could not prove an answer.</summary>
    public static ItsmUserMatchResult Unknown(ItsmRecordLookupReason reason, string provider) =>
        new(ItsmUserMatchStatus.Unknown, reason, null, provider);
}

/// <summary>
/// Strict allow-list for the user name sent to the source: 1-64 ASCII letters, digits, dot, underscore or hyphen,
/// starting with a letter or digit, after trimming outer whitespace. Domain prefixes (<c>\</c>), UPN suffixes
/// (<c>@</c>), quotes, wildcards, filter markers and inner spaces are rejected, so the value is placed in the
/// filter verbatim and cannot widen the match. Case is preserved: the source compares <c>m_username</c>
/// case-sensitively (ADR-0032 amendment 2026-10-14).
/// </summary>
public static class ItsmUserName
{
    /// <summary>Maximum accepted length.</summary>
    public const int MaxLength = 64;

    /// <summary>Returns the trimmed name, or null when it is not an acceptable exact user name.</summary>
    public static string? Normalize(string? value)
    {
        if (value is null || value.Length > MaxLength + 16)
        {
            return null;
        }

        string trimmed = value.Trim();
        if (trimmed.Length is 0 or > MaxLength || !char.IsAsciiLetterOrDigit(trimmed[0]))
        {
            return null;
        }

        foreach (char character in trimmed)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character is not ('.' or '_' or '-'))
            {
                return null;
            }
        }

        return trimmed;
    }
}

/// <summary>
/// The Turuncu Hat registration-number rule (ADR-0032 amendment 2026-10-14, the source team's official rule), applied
/// to the value sent and to the value returned. After trimming outer whitespace the value must be 1-20 ASCII letters
/// or digits; anything else (inner spaces, signs, quotes, wildcards, filter markers, non-ASCII letters or digits) is
/// rejected, so the value is placed in the filter verbatim and cannot widen the match. A value that is not all digits
/// is kept as it is. An all-digit value is brought to the source form: leading zeros are dropped and what remains is
/// left-padded with zeros to <see cref="CanonicalDigits"/> digits (so shorter numbers are padded, six-digit numbers
/// keep their leading zeros, longer numbers lose theirs). A value of only zeros is rejected. The number is personal
/// data used only as a query key; the mapping never returns, logs or stores it.
/// </summary>
public static class ItsmRegistrationNumber
{
    /// <summary>Maximum accepted length, leading zeros included.</summary>
    public const int MaxLength = 20;

    /// <summary>Digit count an all-digit number is padded to.</summary>
    public const int CanonicalDigits = 6;

    /// <summary>Returns the source form of the number, or null when the value is not an acceptable registration number.</summary>
    public static string? Normalize(string? value)
    {
        if (value is null || value.Length > MaxLength + 16)
        {
            return null;
        }

        string trimmed = value.Trim();
        if (trimmed.Length is 0 or > MaxLength || !trimmed.All(char.IsAsciiLetterOrDigit))
        {
            return null;
        }

        if (!trimmed.All(char.IsAsciiDigit))
        {
            return trimmed;
        }

        string significant = trimmed.TrimStart('0');
        return significant.Length == 0 ? null : significant.PadLeft(CanonicalDigits, '0');
    }
}

/// <summary>
/// The combined match rule shared by every provider (ADR-0032 amendment 2026-10-13). The registration number is
/// tried first. An Unknown or Ambiguous registration result is returned as it is: Unknown is not "absent", so it
/// never falls back to the user name. A NotMatched registration result tries the user name. A Matched registration
/// result is cross-checked with the user name: the same user id is Matched; another user id is a conflict and
/// returns Ambiguous (no guess); a NotMatched user name keeps the registration match; an Ambiguous or Unknown user
/// name is returned as it is, because the absence of a conflict is not proven.
/// </summary>
internal static class ItsmUserMatchRules
{
    /// <summary>Applies the rule. <c>Conflict</c> is true only when the two keys matched different users.</summary>
    public static async Task<(ItsmUserMatchResult Result, bool Conflict)> MatchAsync(
        IItsmUserMapping mapping,
        string? registrationNumber,
        string? userName,
        string provider,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        bool hasUserName = !string.IsNullOrWhiteSpace(userName);
        if (string.IsNullOrWhiteSpace(registrationNumber))
        {
            return (hasUserName
                ? await mapping.MatchByUserNameAsync(userName, cancellationToken)
                : ItsmUserMatchResult.Unknown(ItsmRecordLookupReason.InvalidIdentifier, provider), false);
        }

        ItsmUserMatchResult byRegistration = await mapping.MatchByRegistrationNumberAsync(registrationNumber, cancellationToken);
        if (!hasUserName || byRegistration.Status is ItsmUserMatchStatus.Unknown or ItsmUserMatchStatus.Ambiguous)
        {
            return (byRegistration, false);
        }

        ItsmUserMatchResult byUserName = await mapping.MatchByUserNameAsync(userName, cancellationToken);
        if (byRegistration.Status == ItsmUserMatchStatus.NotMatched)
        {
            return (byUserName, false);
        }

        return byUserName.Status switch
        {
            ItsmUserMatchStatus.Matched when string.Equals(
                byUserName.User!.UserId, byRegistration.User!.UserId, StringComparison.Ordinal) => (byRegistration, false),
            ItsmUserMatchStatus.Matched => (ItsmUserMatchResult.Ambiguous(byRegistration.Provider), true),
            ItsmUserMatchStatus.NotMatched => (byRegistration, false),
            _ => (byUserName, false)
        };
    }
}
