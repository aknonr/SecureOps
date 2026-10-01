using System.Globalization;
using System.Net.Http.Json;
using SecureOps.Domain.Announcements;
using SecureOps.Shared.Contracts.Announcements;

namespace SecureOps.Ui.Services;

/// <summary>Typed announcement operations over the existing per-browser API session.</summary>
public sealed class AnnouncementApiClient
{
    private readonly HttpClient _client;
    /// <summary>Attaches the authenticated browser session, never a shared identity.</summary>
    public AnnouncementApiClient(HttpClient client, IApiSessionContext session)
    { _client = client; ApiSessionHeaders.Attach(client, session); }
    /// <summary>Owned persisted preparation history, metadata only.</summary>
    public async Task<PreparationPage> PreparationsAsync(int page, CancellationToken token)
    { using HttpResponseMessage response = await SendAsync($"/preparations?page={page}", null, token); return await ApiResponseReader.ReadBodyAsync<PreparationPage>(response, token); }
    /// <summary>Explicit snapshot preparation or historical read; neither confirms nor sends.</summary>
    public async Task<PreparedAnnouncement> PreparedAsync(Guid id, Guid? draftId, long version, CancellationToken token)
    {
        string query = draftId is null ? "" : $"?draftId={draftId}&version={version}";
        using HttpResponseMessage response = await SendAsync($"/preparations/{id}" + query, null, token, draftId is null ? HttpMethod.Get : HttpMethod.Put);
        return await ApiResponseReader.ReadBodyAsync<PreparedAnnouncement>(response, token);
    }
    /// <summary>Reads one bounded owner-only page.</summary>
    public async Task<AnnouncementPage> ListAsync(int page, CancellationToken token)
    {
        using HttpResponseMessage response = await SendAsync($"?page={page}&pageSize=25", null, token);
        return await ApiResponseReader.ReadBodyAsync<AnnouncementPage>(response, token);
    }
    /// <summary>Reads safe allowlisted metadata only.</summary>
    public async Task<AnnouncementBanner[]> BannersAsync(CancellationToken token, string template = "oco-v1")
    {
        using HttpResponseMessage response = await SendAsync("/banners?templateRevision=" + Uri.EscapeDataString(template), null, token);
        return await ApiResponseReader.ReadBodyAsync<AnnouncementBanner[]>(response, token);
    }
    /// <summary>Reads or explicitly saves a version; no automatic write retry.</summary>
    public async Task<(AnnouncementContent Content, long Version)> DraftAsync(Guid id, long version, AnnouncementContent? content, CancellationToken token)
    {
        using HttpResponseMessage response = await SendAsync($"/{id}?version={version}", content, token);
        if (!long.TryParse(response.Headers.ETag?.Tag.Trim('"'), NumberStyles.None, CultureInfo.InvariantCulture, out long current) || current < 1)
        { throw new SecureOpsApiException(UiProblemFactory.FromResponse(502, null)); }
        return (await ApiResponseReader.ReadBodyAsync<AnnouncementContent>(response, token), current);
    }
    /// <summary>Gets a current saved preview, never an editor rendering.</summary>
    public async Task<string> PreviewAsync(Guid id, long version, CancellationToken token)
    {
        using HttpResponseMessage response = await SendAsync($"/{id}?version={version}&format=html", null, token);
        return await response.Content.ReadAsStringAsync(token);
    }
    /// <summary>Pure transient rendering, no UUID authority or persistence operation.</summary>
    public async Task<(string Html, string[] Missing)> LiveAsync(AnnouncementContent content, CancellationToken token)
    {
        using HttpResponseMessage response = await SendAsync("/preview", content, token, HttpMethod.Post);
        string[] missing = response.Headers.TryGetValues("X-Announcement-Incomplete", out IEnumerable<string>? values)
            ? string.Join(",", values).Split(',', StringSplitOptions.RemoveEmptyEntries) : [];
        return (await response.Content.ReadAsStringAsync(token), missing);
    }
    /// <summary>Returns authenticated MIME bytes and the server-owned safe filename.</summary>
    public async Task<(byte[] Bytes, string Name)> DownloadAsync(Guid id, long version, CancellationToken token)
    {
        using HttpResponseMessage response = await SendAsync($"/{id}?version={version}&format=eml", null, token);
        string? name = (response.Content.Headers.ContentDisposition?.FileNameStar ?? response.Content.Headers.ContentDisposition?.FileName)?.Trim('"');
        if (name != $"announcement-{id:N}-v{version}.eml" || response.Content.Headers.ContentType?.MediaType != "message/rfc822")
        { throw new SecureOpsApiException(UiProblemFactory.FromResponse(502, null)); }
        return (await response.Content.ReadAsByteArrayAsync(token), name);
    }
    /// <summary>Reads protected profile choices through the current browser session.</summary>
    public Task<MaintenanceProfileChoice[]> ProfilesAsync(CancellationToken token) => SourceAsync<MaintenanceProfileChoice[]>("/source/profiles", null, token);
    /// <summary>Source configuration/queue observations, not a live provider call.</summary>
    public Task<AnnouncementSourceReadiness> SourceReadinessAsync(CancellationToken token) => SourceAsync<AnnouncementSourceReadiness>("/source/readiness", null, token);
    /// <summary>Submits explicit retrieval, or reads status without enqueueing.</summary>
    public Task<AnnouncementSourceJobStatus> SourceJobAsync(Guid id, AnnouncementSourceSubmission? submission, CancellationToken token) =>
        SourceAsync<AnnouncementSourceJobStatus>($"/{id}/source/jobs", submission, token);
    /// <summary>Reads a review bound to one job and both concurrency versions.</summary>
    public Task<AnnouncementSourceProposal> SourceProposalAsync(Guid id, Guid job, CancellationToken token, string? reviewedSourceOffset = null) =>
        SourceAsync<AnnouncementSourceProposal>($"/{id}/source/jobs/{job}/proposal"
            + (reviewedSourceOffset is null ? "" : "?reviewedSourceOffset=" + Uri.EscapeDataString(reviewedSourceOffset)), null, token);
    /// <summary>Applies exactly the explicitly reviewed request; never retries with substituted versions.</summary>
    public Task<AnnouncementSourceApplyResult> ApplySourceAsync(Guid id, AnnouncementSourceApply request, CancellationToken token) =>
        SourceAsync<AnnouncementSourceApplyResult>($"/{id}/source/apply", request, token);
    /// <summary>Returns a server-owned exact mail preview; never sends.</summary>
    public Task<AnnouncementMailPreview> MailPreviewAsync(Guid preparationId, string kind, CancellationToken token) =>
        SourceAsync<AnnouncementMailPreview>("/mail/preview", new AnnouncementMailPreviewRequest(preparationId, kind), token);
    /// <summary>One explicit confirmation. A replay token reads the original command, never a second send.</summary>
    public Task<AnnouncementMailStatus> MailConfirmAsync(string previewToken, CancellationToken token) =>
        SourceAsync<AnnouncementMailStatus>("/mail/confirm", new AnnouncementMailConfirmation(previewToken), token);
    /// <summary>Reads owner-only command outcomes; no resend or inferred delivery.</summary>
    public Task<AnnouncementMailStatus[]> MailHistoryAsync(Guid draftId, CancellationToken token) =>
        SourceAsync<AnnouncementMailStatus[]>($"/{draftId}/mail", null, token);
    private async Task<T> SourceAsync<T>(string path, object? input, CancellationToken token)
    {
        using HttpResponseMessage response = await SendAsync(path, input, token, input is null ? HttpMethod.Get : HttpMethod.Post);
        return await ApiResponseReader.ReadBodyAsync<T>(response, token);
    }
    private async Task<HttpResponseMessage> SendAsync(string path, object? content, CancellationToken token, HttpMethod? method = null)
    {
        try
        {
            using HttpRequestMessage request = new(method ?? (content is null ? HttpMethod.Get : HttpMethod.Put), "api/v1/announcements" + path);
            if (content is not null)
            {
                request.Content = JsonContent.Create(content, options: ApiResponseReader.JsonOptions);
            }

            HttpResponseMessage response = await _client.SendAsync(request, token);
            if (response.IsSuccessStatusCode)
            {
                return response;
            }

            using (response)
            { throw await ApiResponseReader.ToExceptionAsync(response, token); }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !token.IsCancellationRequested)
        { throw ApiResponseReader.ToTransportException(ex, token); }
    }
}
