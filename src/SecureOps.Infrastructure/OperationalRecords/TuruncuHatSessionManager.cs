using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SecureOps.Shared.Configuration;
using SecureOps.Shared.Contracts.Api;

namespace SecureOps.Infrastructure.OperationalRecords;

/// <summary>Thread-safe, bounded-lifetime Turuncu Hat session manager.</summary>
public sealed class TuruncuHatSessionManager : ITuruncuHatSessionManager
{
    private const string Provider = "TuruncuHat";
    private readonly HttpClient _httpClient;
    private readonly TuruncuHatOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly EnterpriseIntegrationHealthState _health;
    private readonly EnterpriseIntegrationTelemetry _telemetry;
    private readonly ILogger<TuruncuHatSessionManager> _logger;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly object _stateGate = new();
    private SessionLease? _session;

    /// <summary>Initializes the session manager.</summary>
    public TuruncuHatSessionManager(
        HttpClient httpClient,
        IOptions<TuruncuHatOptions> options,
        TimeProvider timeProvider,
        EnterpriseIntegrationHealthState health,
        EnterpriseIntegrationTelemetry telemetry,
        ILogger<TuruncuHatSessionManager> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _timeProvider = timeProvider;
        _health = health;
        _telemetry = telemetry;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<string> GetSessionAsync(CancellationToken cancellationToken)
    {
        SessionLease? current = Current();
        if (current is not null)
        {
            return current.Value;
        }

        await _refreshGate.WaitAsync(cancellationToken);
        try
        {
            current = Current();
            if (current is not null)
            {
                return current.Value;
            }

            SessionLease created = await LoginAsync(cancellationToken);
            lock (_stateGate)
            {
                _session = created;
            }

            return created.Value;
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    /// <inheritdoc />
    public void Invalidate(string rejectedSession)
    {
        lock (_stateGate)
        {
            if (string.Equals(_session?.Value, rejectedSession, StringComparison.Ordinal))
            {
                _session = null;
            }
        }
    }

    private SessionLease? Current()
    {
        lock (_stateGate)
        {
            return _session is not null && _session.ExpiresAt > _timeProvider.GetUtcNow()
                ? _session
                : null;
        }
    }

    private async Task<SessionLease> LoginAsync(CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        using HttpRequestMessage request = new(HttpMethod.Post, "login")
        {
            Content = JsonContent.Create(new
            {
                req = new
                {
                    _options.Username,
                    _options.Password,
                    _options.TenantId
                }
            }, options: LegacyContractJson.Options)
        };
        ApplyHeaders(request, _options.Authorization);

        try
        {
            using HttpResponseMessage response = await SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                string errorCode = response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                    ? OperationalErrorCodes.OperationalSourceAuthenticationFailed
                    : OperationalErrorCodes.OperationalSourceUnavailable;
                throw Failure(errorCode, retryable: (int)response.StatusCode >= 500);
            }

            using JsonDocument document = await BoundedJsonHttpContent.ReadAsync(
                response.Content,
                _options.MaxResponseBytes,
                cancellationToken);
            if (!document.RootElement.TryGetProperty("LoginResult", out JsonElement resultElement)
                || resultElement.ValueKind != JsonValueKind.String)
            {
                throw Failure(OperationalErrorCodes.OperationalSourceAuthenticationFailed, false);
            }

            string loginResult = resultElement.GetString() ?? string.Empty;
            string[] segments = loginResult.Split('|');
            if (_options.SessionIdSegmentIndex >= segments.Length
                || segments[_options.SessionIdSegmentIndex].Trim().Length <= 1)
            {
                throw Failure(OperationalErrorCodes.OperationalSourceAuthenticationFailed, false);
            }

            string session = loginResult;
            _health.MarkAvailable(Provider);
            _telemetry.RecordOperation(Provider, "login", "success", stopwatch.Elapsed);
            _logger.LogInformation("Turuncu Hat login succeeded.");
            return new SessionLease(session, _timeProvider.GetUtcNow().AddSeconds(_options.SessionLifetimeSeconds));
        }
        catch (ExternalIntegrationException)
        {
            _health.MarkUnavailable(Provider);
            _telemetry.RecordOperation(Provider, "login", "failure", stopwatch.Elapsed);
            _logger.LogWarning("Turuncu Hat login failed safely.");
            throw;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _health.MarkUnavailable(Provider);
            _telemetry.RecordOperation(Provider, "login", "timeout", stopwatch.Elapsed);
            throw Failure(OperationalErrorCodes.OperationalSourceUnavailable, true);
        }
        catch (HttpRequestException)
        {
            _health.MarkUnavailable(Provider);
            _telemetry.RecordOperation(Provider, "login", "unavailable", stopwatch.Elapsed);
            throw Failure(OperationalErrorCodes.OperationalSourceUnavailable, true);
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException)
        {
            _health.MarkUnavailable(Provider);
            _telemetry.RecordOperation(Provider, "login", "invalid-response", stopwatch.Elapsed);
            throw Failure(OperationalErrorCodes.OperationalSourceAuthenticationFailed, false);
        }
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.RequestTimeoutSeconds));
        return await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
    }

    internal static void ApplyHeaders(HttpRequestMessage request, string authorization)
    {
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.TryAddWithoutValidation("Authorization", authorization);
    }

    private static ExternalIntegrationException Failure(string errorCode, bool retryable) =>
        new(errorCode, retryable);

    private sealed record SessionLease(string Value, DateTimeOffset ExpiresAt);
}
