namespace SecureOps.Infrastructure.ItsmRecords;

/// <summary>
/// Exact, read-only mapping of one product user to the ITSM user id (ADR-0032). The caller passes the user name
/// derived from the server-verified session identity only; there is no free-text or person search, and the lookup
/// is a single exact equality on the source user-name column. Zero or several matches, or any failure, is not a
/// mapping, and the consuming flow must stop.
/// </summary>
public interface IItsmUserMapping
{
    /// <summary>
    /// Maps an exact user name. An invalid name returns Unknown/InvalidIdentifier without a remote call.
    /// Caller cancellation propagates as <see cref="OperationCanceledException"/>; a dependency timeout is Unknown.
    /// </summary>
    public Task<ItsmUserMatchResult> MatchByUserNameAsync(string? userName, CancellationToken cancellationToken);
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

/// <summary>Outcome of one user mapping. The requested user name is deliberately not echoed.</summary>
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
/// filter verbatim and cannot widen the match.
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
