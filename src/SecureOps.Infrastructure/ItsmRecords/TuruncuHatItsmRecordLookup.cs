using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SecureOps.Infrastructure.OperationalRecords;
using SecureOps.Shared.Configuration;
using SecureOps.Shared.Contracts.Api;

namespace SecureOps.Infrastructure.ItsmRecords;

/// <summary>
/// Read-only Turuncu Hat adapter for <see cref="IItsmRecordLookup"/> (ADR-0031). It sends one <c>/query</c> per
/// lookup with the shared single-flight session, a fixed select allow-list and an allow-listed number, reads cells
/// by exact <c>SET.</c> key, and maps every failure to Unknown: only a complete, successful response with zero
/// rows is NotFound. It never writes, never selects person fields, and never logs the session, credentials,
/// base URL or the looked-up number.
/// </summary>
public sealed class TuruncuHatItsmRecordLookup(
    HttpClient httpClient,
    ITuruncuHatSessionManager sessions,
    IOptions<TuruncuHatOptions> turuncuHat,
    IOptions<ItsmRecordLookupOptions> lookup,
    EnterpriseIntegrationTelemetry telemetry,
    ILogger<TuruncuHatItsmRecordLookup> logger) : IItsmRecordLookup
{
    /// <summary>Provider name reported in results.</summary>
    public const string ProviderName = "TuruncuHat";

    private const int _maxSourceIdLength = 128;
    private const int _maxTitleLength = 500;
    private const int _maxCodeLength = 256;

    // Person fields (requester, affected user, owner) are deliberately absent. OR status is not selected until a
    // field name is evidenced for SMSS_oRFF; selecting an unproven field could fail every OR lookup.
    private static readonly string[] _orSelects = ["id", "p_code", "p_name", "p_rel_group"];
    private static readonly string[] _ocoSelects =
    [
        "id", "p_code", "p_name", "p_def_status", "p_rel_stage", "p_rel_group",
        "p_proposed_start_date_time", "p_proposed_finish_date_time"
    ];

    /// <inheritdoc />
    public async Task<ItsmRecordLookupResult> LookupAsync(ItsmRecordKind kind, string? number, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string? normalized = ItsmRecordNumber.Normalize(kind, number);
        if (normalized is null)
        {
            return ItsmRecordLookupResult.Unknown(kind, null, ItsmRecordLookupReason.InvalidNumber, ProviderName);
        }

        string? baseObject = kind == ItsmRecordKind.Or ? lookup.Value.OrBaseObject : lookup.Value.OcoBaseObject;
        if (!IsSafeIdentifier(baseObject))
        {
            return Finish(kind, ItsmRecordLookupResult.Unknown(kind, normalized, ItsmRecordLookupReason.NotConfigured, ProviderName),
                Stopwatch.StartNew());
        }

        var stopwatch = Stopwatch.StartNew();
        string[] selects = kind == ItsmRecordKind.Or ? _orSelects : _ocoSelects;
        // The number passed the digit-only allow-list, so it is placed in the filter verbatim; nothing is escaped.
        string[] filters = [string.Concat("#%m_active%#='True' AND #%p_code%#='", normalized, "'")];
        try
        {
            TuruncuHatItsmRecordRows? rows = await QueryAsync(baseObject!, filters, selects, cancellationToken);
            ItsmRecordLookupResult result = rows is null
                ? ItsmRecordLookupResult.Unknown(kind, normalized, ItsmRecordLookupReason.AuthenticationFailed, ProviderName)
                : Interpret(kind, normalized, rows);
            return Finish(kind, result, stopwatch);
        }
        catch (LookupFailure failure)
        {
            return Finish(kind, ItsmRecordLookupResult.Unknown(kind, normalized, failure.Reason, ProviderName), stopwatch);
        }
    }

    private static ItsmRecordLookupResult Interpret(ItsmRecordKind kind, string number, TuruncuHatItsmRecordRows rows)
    {
        if (!rows.Complete || rows.Malformed > 0)
        {
            return ItsmRecordLookupResult.Unknown(kind, number, ItsmRecordLookupReason.IncompleteResponse, ProviderName);
        }

        List<ItsmRecord> records = new(rows.Rows.Count);
        foreach (IReadOnlyDictionary<string, string?> row in rows.Rows)
        {
            ItsmRecord? record = ToRecord(kind, row);
            if (record is null)
            {
                return ItsmRecordLookupResult.Unknown(kind, number, ItsmRecordLookupReason.IncompleteResponse, ProviderName);
            }

            // A row for another number means the filter was not honoured; nothing in this response is trusted.
            if (!string.Equals(record.Code, number, StringComparison.OrdinalIgnoreCase))
            {
                return ItsmRecordLookupResult.Unknown(kind, number, ItsmRecordLookupReason.InvalidResponse, ProviderName);
            }

            records.Add(record);
        }

        return records.Count switch
        {
            0 => ItsmRecordLookupResult.NotFound(kind, number, ProviderName),
            1 => ItsmRecordLookupResult.Found(kind, number, records[0], ProviderName),
            _ => ItsmRecordLookupResult.Ambiguous(kind, number, ProviderName)
        };
    }

    private static ItsmRecord? ToRecord(ItsmRecordKind kind, IReadOnlyDictionary<string, string?> row)
    {
        string? sourceId = row["SET.id"]?.Trim();
        string? code = row["SET.p_code"]?.Trim();
        string title = WebUtility.HtmlDecode(row["SET.p_name"] ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(sourceId) || sourceId.Length > _maxSourceIdLength
            || string.IsNullOrEmpty(code) || code.Length > _maxCodeLength
            || title.Length > _maxTitleLength)
        {
            return null;
        }

        string? group = Code(row["SET.p_rel_group"]);
        if (kind == ItsmRecordKind.Or)
        {
            return new(sourceId, code, title, null, null, group, null, null);
        }

        return new(sourceId, code, title, Code(row["SET.p_def_status"]), Code(row["SET.p_rel_stage"]), group,
            Code(row["SET.p_proposed_start_date_time"]), Code(row["SET.p_proposed_finish_date_time"]));
    }

    private static string? Code(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    // Returns null when the source still rejects the session after one controlled re-login.
    private async Task<TuruncuHatItsmRecordRows?> QueryAsync(
        string baseObject, string[] filters, string[] selects, CancellationToken cancellationToken)
    {
        TuruncuHatOptions wire = turuncuHat.Value;
        string session = await Session(cancellationToken);
        for (int attempt = 0; attempt < 2; attempt++)
        {
            using HttpRequestMessage request = new(HttpMethod.Post, "query")
            {
                Content = JsonContent.Create(new
                {
                    req = new { BaseObject = baseObject, Filters = filters, Selects = selects, SessionID = session, wire.TenantId }
                }, options: LegacyContractJson.Options)
            };
            TuruncuHatSessionManager.ApplyHeaders(request, wire.Authorization);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(wire.RequestTimeoutSeconds));
            try
            {
                using HttpResponseMessage response = await httpClient.SendAsync(
                    request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                {
                    if (attempt == 0)
                    {
                        sessions.Invalidate(session);
                        session = await Session(cancellationToken);
                        continue;
                    }

                    return null;
                }

                if (!response.IsSuccessStatusCode)
                {
                    logger.LogWarning("Turuncu Hat ITSM lookup query failed. StatusCode: {StatusCode}.", (int)response.StatusCode);
                    throw new LookupFailure((int)response.StatusCode >= 500
                        ? ItsmRecordLookupReason.Unavailable
                        : ItsmRecordLookupReason.Rejected);
                }

                using JsonDocument document = await BoundedJsonHttpContent.ReadAsync(
                    response.Content, wire.MaxResponseBytes, timeout.Token);
                return TuruncuHatItsmRecordRowReader.Read(document.RootElement, selects);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new LookupFailure(ItsmRecordLookupReason.Timeout);
            }
            catch (HttpRequestException)
            {
                throw new LookupFailure(ItsmRecordLookupReason.Unavailable);
            }
            catch (TuruncuHatQueryResultException resultException)
            {
                logger.LogWarning("Turuncu Hat ITSM lookup query reported an application error. ErrorNo: {ErrorNo}.",
                    resultException.ErrorNo);
                throw new LookupFailure(ItsmRecordLookupReason.Rejected);
            }
            catch (Exception exception) when (exception is JsonException or InvalidDataException or KeyNotFoundException)
            {
                throw new LookupFailure(ItsmRecordLookupReason.InvalidResponse);
            }
        }

        return null;
    }

    private async Task<string> Session(CancellationToken cancellationToken)
    {
        try
        {
            return await sessions.GetSessionAsync(cancellationToken);
        }
        catch (ExternalIntegrationException exception)
        {
            throw new LookupFailure(
                exception.ErrorCode == OperationalErrorCodes.OperationalSourceAuthenticationFailed
                    ? ItsmRecordLookupReason.AuthenticationFailed
                    : ItsmRecordLookupReason.Unavailable);
        }
    }

    private ItsmRecordLookupResult Finish(ItsmRecordKind kind, ItsmRecordLookupResult result, Stopwatch stopwatch)
    {
        string operation = kind == ItsmRecordKind.Or ? "itsm-lookup-or" : "itsm-lookup-oco";
        string outcome = result.Status == ItsmRecordLookupStatus.Unknown
            ? "unknown-" + result.Reason.ToString().ToLowerInvariant()
            : result.Status.ToString().ToLowerInvariant();
        telemetry.RecordOperation(ProviderName, operation, outcome, stopwatch.Elapsed);
        if (result.Status == ItsmRecordLookupStatus.Unknown)
        {
            logger.LogWarning("Turuncu Hat ITSM lookup could not prove an answer. Kind: {Kind}. Reason: {Reason}.",
                kind, result.Reason);
        }

        return result;
    }

    private static bool IsSafeIdentifier(string? value) =>
        value is { Length: > 0 and <= 128 } && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '_');

    private sealed class LookupFailure(ItsmRecordLookupReason reason) : Exception("Turuncu Hat ITSM lookup failed safely.")
    {
        public ItsmRecordLookupReason Reason { get; } = reason;
    }
}
