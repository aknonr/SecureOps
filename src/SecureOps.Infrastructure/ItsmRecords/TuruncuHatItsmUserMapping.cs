using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SecureOps.Infrastructure.OperationalRecords;
using SecureOps.Shared.Configuration;

namespace SecureOps.Infrastructure.ItsmRecords;

/// <summary>
/// Read-only Turuncu Hat adapter for <see cref="IItsmUserMapping"/> (ADR-0032). It sends one exact-equality
/// <c>/query</c> on the user-name column, selects only the user id, the organization id, the home location id and
/// the user name used to verify the match, and maps every failure to Unknown. It never selects a person name,
/// e-mail, phone number or registration number, and never logs the session, credentials, base URL or the user name.
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

    // m_username is selected only to prove the source honoured the equality filter; it is never returned or logged.
    private static readonly string[] _selects = ["id", "m_username", "p_obs_id", "m_rel_home_lbs_id"];
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

        var stopwatch = Stopwatch.StartNew();
        // The name passed the allow-list, so it is placed in the filter verbatim; nothing is escaped.
        string[] filters = [string.Concat("#%m_username%#='", name, "'")];
        try
        {
            TuruncuHatItsmRecordRows? rows = await _query.QueryAsync(_userObject, filters, _selects, _optional, cancellationToken);
            ItsmUserMatchResult result = rows is null
                ? ItsmUserMatchResult.Unknown(ItsmRecordLookupReason.AuthenticationFailed, ProviderName)
                : Interpret(name, rows);
            return Finish(result, stopwatch);
        }
        catch (ItsmQueryFailure failure)
        {
            return Finish(ItsmUserMatchResult.Unknown(failure.Reason, ProviderName), stopwatch);
        }
    }

    private static ItsmUserMatchResult Interpret(string name, TuruncuHatItsmRecordRows rows)
    {
        if (!rows.Complete || rows.Malformed > 0)
        {
            return ItsmUserMatchResult.Unknown(ItsmRecordLookupReason.IncompleteResponse, ProviderName);
        }

        List<ItsmUserReference> users = new(rows.Rows.Count);
        foreach (IReadOnlyDictionary<string, string?> row in rows.Rows)
        {
            string? userId = row["SET.id"]?.Trim();
            string? returnedName = row["SET.m_username"]?.Trim();
            if (string.IsNullOrEmpty(userId) || userId.Length > _maxTextLength
                || !TryText(row, "SET.p_obs_id", out string? organization)
                || !TryText(row, "SET.m_rel_home_lbs_id", out string? location))
            {
                return ItsmUserMatchResult.Unknown(ItsmRecordLookupReason.IncompleteResponse, ProviderName);
            }

            // A row for another user name means the filter was not honoured; nothing in this response is trusted.
            if (!string.Equals(returnedName, name, StringComparison.OrdinalIgnoreCase))
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

    private ItsmUserMatchResult Finish(ItsmUserMatchResult result, Stopwatch stopwatch)
    {
        string outcome = result.Status == ItsmUserMatchStatus.Unknown
            ? "unknown-" + result.Reason.ToString().ToLowerInvariant()
            : result.Status.ToString().ToLowerInvariant();
        telemetry.RecordOperation(ProviderName, "itsm-user-match", outcome, stopwatch.Elapsed);
        if (result.Status == ItsmUserMatchStatus.Unknown)
        {
            logger.LogWarning("Turuncu Hat ITSM user match could not prove an answer. Reason: {Reason}.", result.Reason);
        }

        return result;
    }
}
