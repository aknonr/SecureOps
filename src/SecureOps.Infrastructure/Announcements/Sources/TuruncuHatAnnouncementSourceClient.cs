using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SecureOps.Infrastructure.OperationalRecords;
using SecureOps.Shared.Configuration;

namespace SecureOps.Infrastructure.Announcements.Sources;

/// <summary>
/// Read-only Turuncu Hat adapter for device service relationships and proposed OCO windows.
/// Values are read by exact semantic key, never by cell position, and every candidate is kept:
/// an ambiguous relationship is reported as ambiguous instead of resolving to the first row.
/// m_active only excludes retired records; it is not evidence of approval or of OCO scope.
/// </summary>
public sealed class TuruncuHatAnnouncementSourceClient(
    HttpClient httpClient,
    ITuruncuHatSessionManager sessions,
    IOptions<TuruncuHatOptions> turuncuHat,
    IOptions<AnnouncementSourceOptions> source,
    ILogger<TuruncuHatAnnouncementSourceClient> logger) : IAnnouncementServiceSourceClient
{
    private const string _startKey = "SET.p_proposed_start_date_time";
    private const string _finishKey = "SET.p_proposed_finish_date_time";

    /// <inheritdoc />
    public async Task<ServiceLookupResult> GetDeviceServicesAsync(string device, CancellationToken cancellationToken)
    {
        AnnouncementSourceOptions settings = source.Value;
        if (!SourceNames.IsDevice(device) || !IsFilterSafe(device)
            || !IsConfigured(settings.ServiceInstanceBaseObject) || !IsConfigured(settings.ServiceNameSelect))
        { throw new AnnouncementSourceException("AnnouncementSourceConfigurationUnavailable", false); }

        string key = "SET." + settings.ServiceNameSelect;
        string filter = ActiveFilter("p_name", device.Trim());
        QueryOutcome outcome = await QueryAsync(settings.ServiceInstanceBaseObject, [filter],
            [settings.ServiceNameSelect], "service-relationship", cancellationToken);
        string[] candidates = [.. outcome.Rows
            .Select(row => Cell(row, key))
            .Where(SourceNames.IsService)
            .Select(value => WebUtility.HtmlDecode(value!).Trim())
            .Where(SourceNames.IsService)
            .Distinct(StringComparer.OrdinalIgnoreCase)];
        if (outcome.Malformed > 0 || !outcome.Complete)
        {
            // A truncated or partly unreadable relationship read can never prove a single service.
            logger.LogWarning("Turuncu Hat service relationship read was incomplete. Malformed: {Malformed}. Complete: {Complete}.",
                outcome.Malformed, outcome.Complete);
            return new(device, candidates, candidates.Length == 0 ? "Failed" : "Ambiguous");
        }
        return new(device, candidates, candidates.Length switch { 0 => "Missing", 1 => "Resolved", _ => "Ambiguous" });
    }

    /// <inheritdoc />
    public async Task<ChangeWindowResult> GetChangeWindowAsync(string ocoReference, CancellationToken cancellationToken)
    {
        AnnouncementSourceOptions settings = source.Value;
        if (!IsOcoReference(ocoReference) || !IsConfigured(settings.ChangeBaseObject))
        { throw new AnnouncementSourceException("AnnouncementSourceConfigurationUnavailable", false); }

        string filter = ActiveFilter("p_code", ocoReference.Trim());
        QueryOutcome outcome = await QueryAsync(settings.ChangeBaseObject, [filter],
            ["p_proposed_start_date_time", "p_proposed_finish_date_time"], "change-window", cancellationToken);
        if (outcome.Malformed > 0 || !outcome.Complete)
        { return new(null, null, "Failed"); }
        if (outcome.Rows.Count == 0)
        { return new(null, null, "Missing"); }
        if (outcome.Rows.Count > 1)
        { return new(null, null, "Ambiguous"); }
        string?[] starts = [.. outcome.Rows.Select(row => Cell(row, _startKey)).Distinct(StringComparer.Ordinal)];
        string?[] finishes = [.. outcome.Rows.Select(row => Cell(row, _finishKey)).Distinct(StringComparer.Ordinal)];
        if (starts.Length > 1 || finishes.Length > 1)
        { return new(null, null, "Ambiguous"); }
        return SourceWindowEvidence.Read(starts[0], finishes[0]);
    }

    // The backend owns the query grammar; values are validated, never escaped into an expression.
    private static string ActiveFilter(string field, string value) =>
        string.Concat("#%m_active%#='True' AND #%", field, "%#='", value, "'");

    private static string? Cell(Dictionary<string, string?> row, string key) =>
        row.TryGetValue(key, out string? value) ? value : null;

    private async Task<QueryOutcome> QueryAsync(string baseObject, string[] filters, string[] selects,
        string operation, CancellationToken cancellationToken)
    {
        TuruncuHatOptions wire = turuncuHat.Value;
        string session = await sessions.GetSessionAsync(cancellationToken);
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
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(wire.RequestTimeoutSeconds));
                using HttpResponseMessage response = await httpClient.SendAsync(request,
                    HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden && attempt == 0)
                {
                    sessions.Invalidate(session);
                    session = await sessions.GetSessionAsync(cancellationToken);
                    continue;
                }
                if (!response.IsSuccessStatusCode)
                {
                    logger.LogWarning("Turuncu Hat announcement query failed. Operation: {Operation}. StatusCode: {StatusCode}.",
                        operation, (int)response.StatusCode);
                    throw new AnnouncementSourceException((int)response.StatusCode >= 500
                        ? "AnnouncementSourceUnavailable" : "AnnouncementSourceRejected", (int)response.StatusCode >= 500);
                }
                using JsonDocument document = await BoundedJsonHttpContent.ReadAsync(response.Content,
                    wire.MaxResponseBytes, timeout.Token);
                return AnnouncementSourceQueryParser.Parse(document.RootElement, selects);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            { throw new AnnouncementSourceException("AnnouncementSourceTimeout", true); }
            catch (HttpRequestException)
            { throw new AnnouncementSourceException("AnnouncementSourceUnavailable", true); }
            catch (TuruncuHatQueryResultException resultException)
            {
                logger.LogWarning("Turuncu Hat announcement query reported an application error. Operation: {Operation}. ErrorNo: {ErrorNo}.",
                    operation, resultException.ErrorNo);
                throw new AnnouncementSourceException("AnnouncementSourceRejected", false);
            }
            catch (Exception exception) when (exception is JsonException or InvalidDataException)
            {
                logger.LogWarning("Turuncu Hat announcement query response was invalid. Operation: {Operation}.", operation);
                throw new AnnouncementSourceException("AnnouncementSourceInvalidResponse", false);
            }
        }
        throw new AnnouncementSourceException("AnnouncementSourceAuthenticationFailed", false);
    }

    private static bool IsConfigured(string? value) => value is { Length: > 0 and <= 128 } && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '_');
    private static bool IsOcoReference(string? value) => value is { Length: > 0 and <= 64 } && value.Trim().Length > 0 && IsFilterSafe(value);
    private static bool IsFilterSafe(string value) =>
        !value.Any(c => char.IsControl(c) || c is '\'' or '"' or '%' or '#' or '\\');
}
