using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SecureOps.Shared.Configuration;
using SecureOps.Shared.Contracts.Api;

namespace SecureOps.Infrastructure.OperationalRecords;

/// <summary>Creates Jira issues using the reviewed legacy field mapping.</summary>
public sealed class CorporateJiraClient : IJiraClient
{
    private const string _provider = "Jira";
    private readonly HttpClient _httpClient;
    private readonly JiraIntegrationOptions _options;
    private readonly OperationalRecordsOptions _operationalOptions;
    private readonly EnterpriseIntegrationHealthState _health;
    private readonly EnterpriseIntegrationTelemetry _telemetry;
    private readonly ILogger<CorporateJiraClient> _logger;

    /// <summary>Initializes the client.</summary>
    public CorporateJiraClient(
        HttpClient httpClient,
        IOptions<JiraIntegrationOptions> options,
        IOptions<OperationalRecordsOptions> operationalOptions,
        EnterpriseIntegrationHealthState health,
        EnterpriseIntegrationTelemetry telemetry,
        ILogger<CorporateJiraClient> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _operationalOptions = operationalOptions.Value;
        _health = health;
        _telemetry = telemetry;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<JiraIssueCreationResult> CreateIssueAsync(JiraIssueDraft draft, CancellationToken cancellationToken)
    {
        if (_operationalOptions.ReadOnlyIntegrationMode)
        {
            throw new ExternalIntegrationException(OperationalErrorCodes.ExternalWritesDisabled, retryable: false);
        }

        JiraIssueFieldMapping mapping = draft.FieldMapping;
        if (draft.ReviewOnly || draft.BlockingConditions.Count > 0
            || draft.RequestType == SecureOps.Domain.OperationalRecords.OperationalRecordClassification.SoftwareInstallation
            || string.IsNullOrWhiteSpace(mapping.IssueTypeId)
            || string.IsNullOrWhiteSpace(mapping.TeamCustomField)
            || string.IsNullOrWhiteSpace(mapping.TeamValue)
            || string.IsNullOrWhiteSpace(mapping.RequesterWatcherCustomField)
            || mapping.Labels.Count == 0)
        {
            throw new ExternalIntegrationException(OperationalErrorCodes.JiraValidationFailed, retryable: false);
        }

        Dictionary<string, object?> fields = new(StringComparer.Ordinal)
        {
            ["project"] = new { key = draft.ProjectKey },
            ["issuetype"] = new { id = mapping.IssueTypeId },
            ["summary"] = draft.Summary,
            ["description"] = draft.Description,
            [mapping.TeamCustomField] = new { value = mapping.TeamValue },
            ["labels"] = mapping.Labels
        };
        if (!string.IsNullOrWhiteSpace(draft.RequesterAccountId))
        {
            fields[mapping.RequesterWatcherCustomField] = new[] { new { name = draft.RequesterAccountId } };
        }
        if (!string.IsNullOrWhiteSpace(draft.AssigneeUsername))
        {
            fields["assignee"] = new { name = draft.AssigneeUsername };
        }
        if (!string.IsNullOrWhiteSpace(draft.ReporterUsername))
        {
            fields["reporter"] = new { name = draft.ReporterUsername };
        }

        var stopwatch = Stopwatch.StartNew();
        using HttpRequestMessage request = new(HttpMethod.Post, "rest/api/2/issue")
        {
            Content = JsonContent.Create(new Dictionary<string, object?> { ["fields"] = fields })
        };
        request.Headers.TryAddWithoutValidation("Authorization", _options.Authorization);
        try
        {
            using HttpResponseMessage response = await SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw Failure(response.StatusCode, draft.ReporterUsername is not null);
            }

            using JsonDocument document = await BoundedJsonHttpContent.ReadAsync(
                response.Content,
                _options.MaxResponseBytes,
                cancellationToken);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("key", out JsonElement keyElement)
                || keyElement.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(keyElement.GetString()))
            {
                throw new ExternalIntegrationException(OperationalErrorCodes.JiraCreateFailed, false, outcomeUnknown: true);
            }

            _health.MarkAvailable(_provider);
            _telemetry.RecordOperation(_provider, "issue-create", "success", stopwatch.Elapsed);
            _logger.LogInformation("Jira issue creation returned a confirmed key.");
            return new JiraIssueCreationResult(keyElement.GetString()!);
        }
        catch (ExternalIntegrationException)
        {
            _health.MarkUnavailable(_provider);
            _telemetry.RecordOperation(_provider, "issue-create", "failure", stopwatch.Elapsed);
            throw;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            MarkUnknown(stopwatch);
            throw new ExternalIntegrationException(OperationalErrorCodes.JiraUnavailable, false, outcomeUnknown: true);
        }
        catch (HttpRequestException)
        {
            MarkUnknown(stopwatch);
            throw new ExternalIntegrationException(OperationalErrorCodes.JiraUnavailable, false, outcomeUnknown: true);
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException)
        {
            MarkUnknown(stopwatch);
            throw new ExternalIntegrationException(OperationalErrorCodes.JiraCreateFailed, false, outcomeUnknown: true);
        }
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.RequestTimeoutSeconds));
        return await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
    }

    private void MarkUnknown(Stopwatch stopwatch)
    {
        _health.MarkUnavailable(_provider);
        _telemetry.RecordOperation(_provider, "issue-create", "outcome-unknown", stopwatch.Elapsed);
        _logger.LogWarning("Jira issue creation outcome is unknown; reconciliation is required.");
    }

    private static ExternalIntegrationException Failure(HttpStatusCode statusCode, bool reporterSpecified) => statusCode switch
    {
        HttpStatusCode.BadRequest or HttpStatusCode.Forbidden when reporterSpecified =>
            new(OperationalErrorCodes.JiraReporterRejected, false),
        HttpStatusCode.BadRequest => new(OperationalErrorCodes.JiraValidationFailed, false),
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => new(OperationalErrorCodes.JiraUnauthorized, false),
        HttpStatusCode.TooManyRequests => new(OperationalErrorCodes.JiraUnavailable, true),
        _ when (int)statusCode >= 500 => new(OperationalErrorCodes.JiraUnavailable, false, outcomeUnknown: true),
        _ => new(OperationalErrorCodes.JiraCreateFailed, false)
    };
}
