using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SecureOps.Infrastructure.OperationalRecords;
using SecureOps.Shared.Configuration;
using SecureOps.Shared.Contracts.Api;

namespace SecureOps.Infrastructure.ItsmRecords;

/// <summary>
/// The one read-only <c>/query</c> path shared by the ITSM lookup, tracking and user-match adapters (ADR-0031,
/// ADR-0032). It uses the shared single-flight session, sends <c>NoEncode=true</c> (the vendor encodes Turkish
/// characters otherwise) and never sends <c>QueryPermissions</c> (the source default is SELECT). Every failure is
/// a <see cref="ItsmQueryFailure"/> carrying a bounded reason; it never logs the session, credentials, base URL,
/// filters or any value read.
/// </summary>
internal sealed class TuruncuHatItsmQueryClient(
    HttpClient httpClient,
    ITuruncuHatSessionManager sessions,
    TuruncuHatOptions wire,
    ILogger logger)
{
    /// <summary>
    /// Runs one query. Returns null when the source still rejects the session after one controlled re-login.
    /// Throws <see cref="ItsmQueryFailure"/> for every other failure; caller cancellation propagates.
    /// </summary>
    public async Task<TuruncuHatItsmRecordRows?> QueryAsync(
        string baseObject,
        string[] filters,
        string[] selects,
        IReadOnlyCollection<string>? optionalSelects,
        CancellationToken cancellationToken)
    {
        string session = await Session(cancellationToken);
        for (int attempt = 0; attempt < 2; attempt++)
        {
            using HttpRequestMessage request = new(HttpMethod.Post, "query")
            {
                Content = JsonContent.Create(new
                {
                    req = new { BaseObject = baseObject, Filters = filters, Selects = selects, NoEncode = true, SessionID = session, wire.TenantId }
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
                    logger.LogWarning("Turuncu Hat ITSM query failed. StatusCode: {StatusCode}.", (int)response.StatusCode);
                    throw new ItsmQueryFailure((int)response.StatusCode >= 500
                        ? ItsmRecordLookupReason.Unavailable
                        : ItsmRecordLookupReason.Rejected);
                }

                using JsonDocument document = await BoundedJsonHttpContent.ReadAsync(
                    response.Content, wire.MaxResponseBytes, timeout.Token, TimeSpan.FromSeconds(wire.RequestTimeoutSeconds));
                return TuruncuHatItsmRecordRowReader.Read(document.RootElement, selects, optionalSelects);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new ItsmQueryFailure(ItsmRecordLookupReason.Timeout);
            }
            catch (HttpRequestException)
            {
                throw new ItsmQueryFailure(ItsmRecordLookupReason.Unavailable);
            }
            catch (TuruncuHatQueryResultException resultException)
            {
                logger.LogWarning("Turuncu Hat ITSM query reported an application error. ErrorNo: {ErrorNo}.",
                    resultException.ErrorNo);
                throw new ItsmQueryFailure(ItsmRecordLookupReason.Rejected);
            }
            catch (Exception exception) when (exception is JsonException or InvalidDataException or KeyNotFoundException)
            {
                throw new ItsmQueryFailure(ItsmRecordLookupReason.InvalidResponse);
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
            throw new ItsmQueryFailure(
                exception.ErrorCode == OperationalErrorCodes.OperationalSourceAuthenticationFailed
                    ? ItsmRecordLookupReason.AuthenticationFailed
                    : ItsmRecordLookupReason.Unavailable);
        }
    }

    /// <summary>Strict identifier allow-list used for configured object and field names.</summary>
    public static bool IsSafeIdentifier(string? value) =>
        value is { Length: > 0 and <= 128 } && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '_');
}

/// <summary>A query that could not prove an answer; carries only a bounded, secret-free reason.</summary>
internal sealed class ItsmQueryFailure(ItsmRecordLookupReason reason) : Exception("Turuncu Hat ITSM query failed safely.")
{
    public ItsmRecordLookupReason Reason { get; } = reason;
}
