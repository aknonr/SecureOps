using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SecureOps.Shared.Configuration;

namespace SecureOps.Infrastructure.OperationalRecords;

/// <summary>Resolves one exact Jira user from the evidenced username search endpoint.</summary>
public sealed class CorporateJiraRequesterResolver : IRequesterResolver
{
    private const string Provider = "Jira";
    private readonly HttpClient _httpClient;
    private readonly JiraIntegrationOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly EnterpriseIntegrationHealthState _health;
    private readonly EnterpriseIntegrationTelemetry _telemetry;
    private readonly ILogger<CorporateJiraRequesterResolver> _logger;

    /// <summary>Initializes the resolver.</summary>
    public CorporateJiraRequesterResolver(
        HttpClient httpClient,
        IOptions<JiraIntegrationOptions> options,
        TimeProvider timeProvider,
        EnterpriseIntegrationHealthState health,
        EnterpriseIntegrationTelemetry telemetry,
        ILogger<CorporateJiraRequesterResolver> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _timeProvider = timeProvider;
        _health = health;
        _telemetry = telemetry;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<RequesterResolutionResult> ResolveExactAsync(string identity, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(identity) || identity.Length > 256)
        {
            return RequesterResolutionResult.NotFound();
        }

        for (int attempt = 1; attempt <= _options.UserSearchMaxAttempts; attempt++)
        {
            var stopwatch = Stopwatch.StartNew();
            try
            {
                using HttpRequestMessage request = new(
                    HttpMethod.Get,
                    $"rest/api/2/user/search?username={Uri.EscapeDataString(identity)}");
                ApplyAuthorization(request);
                using HttpResponseMessage response = await SendAsync(request, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    if (IsRetryable(response.StatusCode) && attempt < _options.UserSearchMaxAttempts)
                    {
                        await DelayAsync(cancellationToken);
                        continue;
                    }

                    _health.MarkUnavailable(Provider);
                    _telemetry.RecordOperation(Provider, "user-search", "failure", stopwatch.Elapsed);
                    return RequesterResolutionResult.Failed();
                }

                using JsonDocument document = await BoundedJsonHttpContent.ReadAsync(
                    response.Content,
                    _options.MaxResponseBytes,
                    cancellationToken);
                RequesterResolutionResult result = ParseExact(document.RootElement, identity);
                _health.MarkAvailable(Provider);
                _telemetry.RecordOperation(Provider, "user-search", result.Status.ToString(), stopwatch.Elapsed);
                return result;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                if (attempt < _options.UserSearchMaxAttempts)
                {
                    await DelayAsync(cancellationToken);
                    continue;
                }
            }
            catch (Exception exception) when (exception is HttpRequestException or JsonException or InvalidDataException)
            {
                if (exception is HttpRequestException && attempt < _options.UserSearchMaxAttempts)
                {
                    await DelayAsync(cancellationToken);
                    continue;
                }
            }

            _health.MarkUnavailable(Provider);
            _telemetry.RecordOperation(Provider, "user-search", "failure", stopwatch.Elapsed);
            _logger.LogWarning("Jira exact user resolution failed safely after {AttemptCount} attempts.", attempt);
            return RequesterResolutionResult.Failed();
        }

        return RequesterResolutionResult.Failed();
    }

    internal static RequesterResolutionResult ParseExact(JsonElement root, string identity)
    {
        if (root.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("Jira user-search response did not match the reviewed contract.");
        }

        List<(string Name, string DisplayName)> candidates = [];
        int malformed = 0;
        foreach (JsonElement item in root.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Object
                && item.TryGetProperty("name", out JsonElement nameElement)
                && nameElement.ValueKind == JsonValueKind.String
                && item.TryGetProperty("displayName", out JsonElement displayElement)
                && displayElement.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(nameElement.GetString())
                && !string.IsNullOrWhiteSpace(displayElement.GetString()))
            {
                candidates.Add((nameElement.GetString()!, displayElement.GetString()!));
            }
            else
            {
                malformed++;
            }
        }

        if (malformed > 0)
        {
            throw new InvalidDataException("Jira user-search response contained an invalid user projection.");
        }

        (string Name, string DisplayName)[] exactName = candidates
            .Where(candidate => string.Equals(candidate.Name, identity, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        (string Name, string DisplayName)[] exact = exactName.Length > 0
            ? exactName
            : candidates.Where(candidate => string.Equals(candidate.DisplayName, identity, StringComparison.Ordinal)).ToArray();
        return exact.Length switch
        {
            0 => RequesterResolutionResult.NotFound(),
            1 => RequesterResolutionResult.Found(exact[0].Name),
            _ => RequesterResolutionResult.Ambiguous()
        };
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.RequestTimeoutSeconds));
        return await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
    }

    private Task DelayAsync(CancellationToken cancellationToken) => Task.Delay(
        TimeSpan.FromMilliseconds(_options.UserSearchRetryDelayMilliseconds),
        _timeProvider,
        cancellationToken);

    private void ApplyAuthorization(HttpRequestMessage request) =>
        request.Headers.TryAddWithoutValidation("Authorization", _options.Authorization);

    private static bool IsRetryable(HttpStatusCode code) => code == HttpStatusCode.TooManyRequests || (int)code >= 500;
}
