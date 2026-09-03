using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SecureOps.Shared.Configuration;
using SecureOps.Shared.Contracts.Api;

namespace SecureOps.Infrastructure.OperationalRecords;

/// <summary>Typed Turuncu Hat Operational Record and BPM activity provider.</summary>
public sealed class TuruncuHatOperationalRecordClient : IOperationalRecordClient
{
    private const string Provider = "TuruncuHat";
    private static readonly string[] _sourceSelects = ["id", "p_code", "p_name", "p_description", "p_rel_requester"];
    private static readonly string[] _activitySelects = ["id", "m_created_dt"];
    private readonly HttpClient _httpClient;
    private readonly ITuruncuHatSessionManager _sessions;
    private readonly TuruncuHatOptions _options;
    private readonly OperationalRecordsOptions _operationalOptions;
    private readonly EnterpriseIntegrationHealthState _health;
    private readonly EnterpriseIntegrationTelemetry _telemetry;
    private readonly ILogger<TuruncuHatOperationalRecordClient> _logger;

    /// <summary>Initializes the client.</summary>
    public TuruncuHatOperationalRecordClient(
        HttpClient httpClient,
        ITuruncuHatSessionManager sessions,
        IOptions<TuruncuHatOptions> options,
        IOptions<OperationalRecordsOptions> operationalOptions,
        EnterpriseIntegrationHealthState health,
        EnterpriseIntegrationTelemetry telemetry,
        ILogger<TuruncuHatOperationalRecordClient> logger)
    {
        _httpClient = httpClient;
        _sessions = sessions;
        _options = options.Value;
        _operationalOptions = operationalOptions.Value;
        _health = health;
        _telemetry = telemetry;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OperationalRecordSourceItem>> GetActiveAsync(
        int maximumCount,
        CancellationToken cancellationToken)
    {
        ParsedSourceRecords parsed = await QuerySourceAsync(sourceRecordId: null, cancellationToken);
        return parsed.Items.Take(maximumCount).ToArray();
    }

    /// <inheritdoc />
    public async Task<OperationalRecordSourceItem?> GetByIdAsync(
        string sourceRecordId,
        CancellationToken cancellationToken)
    {
        if (!long.TryParse(sourceRecordId, NumberStyles.None, CultureInfo.InvariantCulture, out long numericSourceRecordId)
            || numericSourceRecordId <= 0)
        {
            return null;
        }

        ParsedSourceRecords parsed = await QuerySourceAsync(numericSourceRecordId, cancellationToken);
        return parsed.Items.SingleOrDefault(item =>
            string.Equals(item.SourceRecordId, sourceRecordId, StringComparison.Ordinal));
    }

    /// <inheritdoc />
    public async Task CloseAsync(
        string sourceRecordId,
        string orCode,
        string jiraIssueKey,
        CancellationToken cancellationToken)
    {
        if (_operationalOptions.ReadOnlyIntegrationMode)
        {
            throw new ExternalIntegrationException(OperationalErrorCodes.ExternalWritesDisabled, retryable: false);
        }

        if (!long.TryParse(sourceRecordId, NumberStyles.None, CultureInfo.InvariantCulture, out long sourceId)
            || sourceId <= 0)
        {
            throw new ExternalIntegrationException(OperationalErrorCodes.OperationalRecordCloseFailed, false);
        }

        string activityFilter =
            $"#%m_actvty_task_model%#={_options.ActivityTaskModelId} AND #%m_status%#=1 "
            + $"AND #%m_process.m_main_object_id%#={sourceId} AND #%m_group%#={_options.ActivityGroupId} "
            + $"AND #%m_process.m_main_object%#={_options.ActivityMainObjectTypeId} AND #%m_active%#='True'";
        using JsonDocument activityResponse = await QueryAsync(
            _options.ActivityBaseObject,
            [activityFilter],
            _activitySelects,
            "activity-query",
            cancellationToken);
        IReadOnlyList<string> activityIds = TuruncuHatQueryParser.ParseActivityIds(
            activityResponse.RootElement,
            _activitySelects);
        if (activityIds.Count == 0)
        {
            throw new ExternalIntegrationException(OperationalErrorCodes.OperationalRecordActivityNotFound, false);
        }

        if (activityIds.Count != 1
            || !long.TryParse(activityIds[0], NumberStyles.None, CultureInfo.InvariantCulture, out long activityId)
            || activityId <= 0)
        {
            throw new ExternalIntegrationException(OperationalErrorCodes.OperationalRecordActivityAmbiguous, false);
        }

        string comment = _options.CompletionCommentTemplate.Replace("{JiraKey}", jiraIssueKey, StringComparison.Ordinal);
        await UpdateActivityAsync(activityId, comment, cancellationToken);
    }

    private async Task<ParsedSourceRecords> QuerySourceAsync(long? sourceRecordId, CancellationToken cancellationToken)
    {
        string excluded = string.Join(',', _options.ExcludedDccIds.Order());
        string filter =
            $"#%m_active%#='True' AND #%p_dcc%# NOT IN ({excluded}) "
            + $"AND #%p_rel_group%# IN ({_options.RelatedGroupId})";
        if (sourceRecordId.HasValue)
        {
            filter += $" AND #%id%#={sourceRecordId.Value}";
        }

        using JsonDocument response = await QueryAsync(
            _options.SourceBaseObject,
            [filter],
            _sourceSelects,
            "source-query",
            cancellationToken);
        ParsedSourceRecords parsed;
        try
        {
            parsed = TuruncuHatQueryParser.ParseSource(
                response.RootElement,
                _options.MaxDescriptionLength,
                _sourceSelects);
        }
        catch (TuruncuHatQueryResultException)
        {
            _health.MarkUnavailable(Provider);
            _logger.LogWarning(
                "Turuncu Hat source query reported an application error. BaseObject: {BaseObject}. SelectCount: {SelectCount}. FilterCount: 1.",
                _options.SourceBaseObject,
                _sourceSelects.Length);
            throw new ExternalIntegrationException(OperationalErrorCodes.OperationalRecordQueryFailed, false);
        }
        catch (Exception exception) when (exception is InvalidDataException or JsonException)
        {
            _health.MarkUnavailable(Provider);
            _logger.LogWarning(
                "Turuncu Hat source query response parsing failed. BaseObject: {BaseObject}. SelectCount: {SelectCount}. FilterCount: 1.",
                _options.SourceBaseObject,
                _sourceSelects.Length);
            throw new ExternalIntegrationException(OperationalErrorCodes.OperationalRecordQueryFailed, false);
        }

        _telemetry.RecordRecords(Provider, "source-query", parsed.Items.Count, parsed.MalformedCount);
        _logger.LogInformation(
            "Turuncu Hat source query completed. Records: {RecordCount}. MalformedOrAmbiguous: {MalformedCount}.",
            parsed.Items.Count,
            parsed.MalformedCount);
        return parsed;
    }

    private async Task<JsonDocument> QueryAsync(
        string baseObject,
        IReadOnlyList<string> filters,
        IReadOnlyList<string> selects,
        string operation,
        CancellationToken cancellationToken)
    {
        string session = await _sessions.GetSessionAsync(cancellationToken);
        for (int attempt = 0; attempt < 2; attempt++)
        {
            var stopwatch = Stopwatch.StartNew();
            using HttpRequestMessage request = CreateQueryRequest(baseObject, filters, selects, session);
            try
            {
                using HttpResponseMessage response = await SendAsync(request, cancellationToken);
                if ((response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) && attempt == 0)
                {
                    _sessions.Invalidate(session);
                    session = await _sessions.GetSessionAsync(cancellationToken);
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning(
                        "Turuncu Hat query HTTP failure. Operation: {Operation}. BaseObject: {BaseObject}. FilterCount: {FilterCount}. SelectCount: {SelectCount}. StatusCode: {StatusCode}.",
                        operation,
                        baseObject,
                        filters.Count,
                        selects.Count,
                        (int)response.StatusCode);
                    throw QueryFailure(response.StatusCode);
                }

                JsonDocument document = await BoundedJsonHttpContent.ReadAsync(
                    response.Content,
                    _options.MaxResponseBytes,
                    cancellationToken);
                _health.MarkAvailable(Provider);
                _telemetry.RecordOperation(Provider, operation, "success", stopwatch.Elapsed);
                return document;
            }
            catch (ExternalIntegrationException)
            {
                _health.MarkUnavailable(Provider);
                _telemetry.RecordOperation(Provider, operation, "failure", stopwatch.Elapsed);
                throw;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                _health.MarkUnavailable(Provider);
                _telemetry.RecordOperation(Provider, operation, "timeout", stopwatch.Elapsed);
                _logger.LogWarning(
                    "Turuncu Hat query timed out. Operation: {Operation}. BaseObject: {BaseObject}. FilterCount: {FilterCount}. SelectCount: {SelectCount}.",
                    operation,
                    baseObject,
                    filters.Count,
                    selects.Count);
                throw new ExternalIntegrationException(OperationalErrorCodes.OperationalSourceUnavailable, true);
            }
            catch (HttpRequestException)
            {
                _health.MarkUnavailable(Provider);
                _telemetry.RecordOperation(Provider, operation, "unavailable", stopwatch.Elapsed);
                _logger.LogWarning(
                    "Turuncu Hat query transport failed. Operation: {Operation}. BaseObject: {BaseObject}. FilterCount: {FilterCount}. SelectCount: {SelectCount}.",
                    operation,
                    baseObject,
                    filters.Count,
                    selects.Count);
                throw new ExternalIntegrationException(OperationalErrorCodes.OperationalSourceUnavailable, true);
            }
            catch (Exception exception) when (exception is JsonException or InvalidDataException)
            {
                _health.MarkUnavailable(Provider);
                _telemetry.RecordOperation(Provider, operation, "invalid-response", stopwatch.Elapsed);
                _logger.LogWarning(
                    "Turuncu Hat query returned invalid JSON or exceeded response bounds. Operation: {Operation}. BaseObject: {BaseObject}. FilterCount: {FilterCount}. SelectCount: {SelectCount}.",
                    operation,
                    baseObject,
                    filters.Count,
                    selects.Count);
                throw new ExternalIntegrationException(OperationalErrorCodes.OperationalRecordQueryFailed, false);
            }
        }

        throw new ExternalIntegrationException(OperationalErrorCodes.OperationalSourceAuthenticationFailed, false);
    }

    private async Task UpdateActivityAsync(long activityId, string comment, CancellationToken cancellationToken)
    {
        string session = await _sessions.GetSessionAsync(cancellationToken);
        var stopwatch = Stopwatch.StartNew();
        using HttpRequestMessage request = new(HttpMethod.Post, "update")
        {
            Content = JsonContent.Create(new
            {
                req = new
                {
                    BaseObject = _options.ActivityBaseObject,
                    Filters = new[] { $"#%id%#={activityId}" },
                    Updates = new[] { "m_status", _options.CompletedStatusId.ToString(CultureInfo.InvariantCulture), "m_comments", comment },
                    SessionID = session,
                    _options.TenantId
                }
            }, options: LegacyContractJson.Options)
        };
        TuruncuHatSessionManager.ApplyHeaders(request, _options.Authorization);
        try
        {
            using HttpResponseMessage response = await SendAsync(request, cancellationToken);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                _sessions.Invalidate(session);
                throw new ExternalIntegrationException(OperationalErrorCodes.OperationalSourceAuthenticationFailed, false);
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new ExternalIntegrationException(OperationalErrorCodes.OperationalRecordCloseFailed, true);
            }

            using JsonDocument document = await BoundedJsonHttpContent.ReadAsync(
                response.Content,
                _options.MaxResponseBytes,
                cancellationToken);
            if (!document.RootElement.TryGetProperty("UpdateResult", out JsonElement updateResult)
                || updateResult.ValueKind != JsonValueKind.Object
                || !updateResult.TryGetProperty("Success", out JsonElement success)
                || success.ValueKind is not (JsonValueKind.True or JsonValueKind.False)
                || !success.GetBoolean())
            {
                throw new ExternalIntegrationException(OperationalErrorCodes.OperationalRecordCloseFailed, true);
            }

            _health.MarkAvailable(Provider);
            _telemetry.RecordOperation(Provider, "activity-update", "success", stopwatch.Elapsed);
            _logger.LogInformation("Turuncu Hat source completion succeeded.");
        }
        catch (ExternalIntegrationException)
        {
            _health.MarkUnavailable(Provider);
            _telemetry.RecordOperation(Provider, "activity-update", "failure", stopwatch.Elapsed);
            _logger.LogWarning("Turuncu Hat source completion failed safely.");
            throw;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _health.MarkUnavailable(Provider);
            _telemetry.RecordOperation(Provider, "activity-update", "timeout", stopwatch.Elapsed);
            throw new ExternalIntegrationException(OperationalErrorCodes.OperationalRecordCloseFailed, true);
        }
        catch (HttpRequestException)
        {
            _health.MarkUnavailable(Provider);
            _telemetry.RecordOperation(Provider, "activity-update", "unavailable", stopwatch.Elapsed);
            throw new ExternalIntegrationException(OperationalErrorCodes.OperationalRecordCloseFailed, true);
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException)
        {
            _health.MarkUnavailable(Provider);
            _telemetry.RecordOperation(Provider, "activity-update", "invalid-response", stopwatch.Elapsed);
            throw new ExternalIntegrationException(OperationalErrorCodes.OperationalRecordCloseFailed, true);
        }
    }

    private HttpRequestMessage CreateQueryRequest(
        string baseObject,
        IReadOnlyList<string> filters,
        IReadOnlyList<string> selects,
        string session)
    {
        HttpRequestMessage request = new(HttpMethod.Post, "query")
        {
            Content = JsonContent.Create(new
            {
                req = new
                {
                    BaseObject = baseObject,
                    Filters = filters,
                    Selects = selects,
                    SessionID = session,
                    _options.TenantId
                }
            }, options: LegacyContractJson.Options)
        };
        TuruncuHatSessionManager.ApplyHeaders(request, _options.Authorization);
        return request;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.RequestTimeoutSeconds));
        return await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
    }

    private static ExternalIntegrationException QueryFailure(HttpStatusCode statusCode) => statusCode switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
            new ExternalIntegrationException(OperationalErrorCodes.OperationalSourceAuthenticationFailed, false),
        HttpStatusCode.TooManyRequests =>
            new ExternalIntegrationException(OperationalErrorCodes.OperationalSourceUnavailable, true),
        _ when (int)statusCode >= 500 =>
            new ExternalIntegrationException(OperationalErrorCodes.OperationalSourceUnavailable, true),
        _ => new ExternalIntegrationException(OperationalErrorCodes.OperationalRecordQueryFailed, false)
    };
}
