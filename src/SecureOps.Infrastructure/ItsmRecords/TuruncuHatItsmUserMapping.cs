using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SecureOps.Infrastructure.OperationalRecords;
using SecureOps.Shared.Configuration;

namespace SecureOps.Infrastructure.ItsmRecords;

/// <summary>
/// Read-only Turuncu Hat adapter for <see cref="IItsmUserMapping"/> (ADR-0032 and its 2026-10-13 and 2026-10-14
/// amendments). Each key is one exact-equality <c>/query</c> on the active users of <c>cmn_user</c>
/// (<c>m_active='True'</c>; the source keeps passive users): the user-name column or the registration-number column.
/// It selects only the user id, the organization id, the home location id and the key column used to verify the
/// match, and maps every failure to Unknown. It never selects a person name, e-mail or phone number, never returns
/// the key, and never logs the session, credentials, base URL, user name or registration number.
/// </summary>
public sealed class TuruncuHatItsmUserMapping(
    HttpClient httpClient,
    ITuruncuHatSessionManager sessions,
    IOptions<TuruncuHatOptions> turuncuHat,
    EnterpriseIntegrationTelemetry telemetry,
    ILogger<TuruncuHatItsmUserMapping> logger) : IItsmUserMapping
{
    /// <summary>Provider name reported in results.</summary>
    public const string ProviderName = "TuruncuHat";

    private const string _userObject = "cmn_user";
    private const int _maxTextLength = 128;

    // The key column is selected only to prove the source honoured the equality filter; it is never returned or logged.
    private static readonly string[] _userNameSelects = ["id", "m_username", "p_obs_id", "m_rel_home_lbs_id"];
    private static readonly string[] _registrationSelects = ["id", "c_registration_number", "p_obs_id", "m_rel_home_lbs_id"];
    private static readonly string[] _optional = ["p_obs_id", "m_rel_home_lbs_id"];

    private readonly TuruncuHatItsmQueryClient _query = new(httpClient, sessions, turuncuHat.Value, logger);

    /// <inheritdoc />
    public async Task<ItsmUserMatchResult> MatchByUserNameAsync(string? userName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string? name = ItsmUserName.Normalize(userName);
        if (name is null)
        {
            return ItsmUserMatchResult.Unknown(ItsmRecordLookupReason.InvalidIdentifier, ProviderName);
        }

        // The name passed the allow-list, so it is placed in the filter verbatim, case preserved; nothing is escaped.
        // The source compares m_username case-sensitively, so the returned value must match ordinally as well.
        return await QueryAsync(new KeyQuery("itsm-user-match", "user name",
            ActiveUserFilter("m_username", name), _userNameSelects, "SET.m_username",
            returned => string.Equals(returned?.Trim(), name, StringComparison.Ordinal)), cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ItsmUserMatchResult> MatchByRegistrationNumberAsync(string? registrationNumber, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string? number = ItsmRegistrationNumber.Normalize(registrationNumber);
        if (number is null)
        {
            return ItsmUserMatchResult.Unknown(ItsmRecordLookupReason.InvalidIdentifier, ProviderName);
        }

        // The number passed the letter-or-digit allow-list and is in the source form, so it is placed in the filter
        // verbatim. The returned value is brought to the same form, so a source that returns it as a number (without
        // its leading zeros) still verifies.
        return await QueryAsync(new KeyQuery("itsm-user-match-registration", "registration number",
            ActiveUserFilter("c_registration_number", number), _registrationSelects, "SET.c_registration_number",
            returned => string.Equals(ItsmRegistrationNumber.Normalize(returned), number, StringComparison.Ordinal)), cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ItsmUserMatchResult> MatchAsync(string? registrationNumber, string? userName, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        (ItsmUserMatchResult result, bool conflict) = await ItsmUserMatchRules.MatchAsync(
            this, registrationNumber, userName, ProviderName, cancellationToken);
        telemetry.RecordOperation(ProviderName, "itsm-user-match-combined", conflict ? "conflict" : Outcome(result), stopwatch.Elapsed);
        if (conflict)
        {
            logger.LogWarning("Turuncu Hat ITSM user match found the registration number and the user name on different users; no mapping is made.");
        }

        return result;
    }

    private async Task<ItsmUserMatchResult> QueryAsync(KeyQuery key, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            TuruncuHatItsmRecordRows? rows = await _query.QueryAsync(_userObject, [key.Filter], key.Selects, _optional, cancellationToken);
            ItsmUserMatchResult result = rows is null
                ? ItsmUserMatchResult.Unknown(ItsmRecordLookupReason.AuthenticationFailed, ProviderName)
                : Interpret(key, rows);
            return Finish(key, result, stopwatch);
        }
        catch (ItsmQueryFailure failure)
        {
            return Finish(key, ItsmUserMatchResult.Unknown(failure.Reason, ProviderName), stopwatch);
        }
    }

    private static ItsmUserMatchResult Interpret(KeyQuery key, TuruncuHatItsmRecordRows rows)
    {
        if (!rows.Complete || rows.Malformed > 0)
        {
            return ItsmUserMatchResult.Unknown(ItsmRecordLookupReason.IncompleteResponse, ProviderName);
        }

        List<ItsmUserReference> users = new(rows.Rows.Count);
        foreach (IReadOnlyDictionary<string, string?> row in rows.Rows)
        {
            string? userId = row["SET.id"]?.Trim();
            if (string.IsNullOrEmpty(userId) || userId.Length > _maxTextLength
                || !TryText(row, "SET.p_obs_id", out string? organization)
                || !TryText(row, "SET.m_rel_home_lbs_id", out string? location))
            {
                return ItsmUserMatchResult.Unknown(ItsmRecordLookupReason.IncompleteResponse, ProviderName);
            }

            // A row for another key value means the filter was not honoured; nothing in this response is trusted.
            if (!key.Verify(row[key.ReturnedKey]))
            {
                return ItsmUserMatchResult.Unknown(ItsmRecordLookupReason.InvalidResponse, ProviderName);
            }

            users.Add(new ItsmUserReference(userId, organization, location));
        }

        return users.Count switch
        {
            0 => ItsmUserMatchResult.NotMatched(ProviderName),
            1 => ItsmUserMatchResult.Matched(users[0], ProviderName),
            _ => ItsmUserMatchResult.Ambiguous(ProviderName)
        };
    }

    private static bool TryText(IReadOnlyDictionary<string, string?> row, string key, out string? value)
    {
        string? text = row.TryGetValue(key, out string? raw) ? raw?.Trim() : null;
        value = string.IsNullOrEmpty(text) ? null : text;
        return text is not { Length: > _maxTextLength };
    }

    // The key value has passed its allow-list; passive users are excluded by the source filter.
    private static string ActiveUserFilter(string column, string key) =>
        string.Concat("#%m_active%#='True' AND #%", column, "%#='", key, "'");

    private static string Outcome(ItsmUserMatchResult result) => result.Status == ItsmUserMatchStatus.Unknown
        ? "unknown-" + result.Reason.ToString().ToLowerInvariant()
        : result.Status.ToString().ToLowerInvariant();

    private ItsmUserMatchResult Finish(KeyQuery key, ItsmUserMatchResult result, Stopwatch stopwatch)
    {
        telemetry.RecordOperation(ProviderName, key.Operation, Outcome(result), stopwatch.Elapsed);
        if (result.Status == ItsmUserMatchStatus.Unknown)
        {
            // Only the key kind is logged, never its value.
            logger.LogWarning("Turuncu Hat ITSM user match could not prove an answer. Key: {Key}. Reason: {Reason}.",
                key.Kind, result.Reason);
        }

        return result;
    }

    // One exact-equality query: telemetry operation, key kind for logs, filter, selects and the check of the returned key.
    // The filter carries the key value, so ToString names only the kind.
    private sealed record KeyQuery(
        string Operation, string Kind, string Filter, string[] Selects, string ReturnedKey, Func<string?, bool> Verify)
    {
        public override string ToString() => Kind;
    }
}
