using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SecureOps.Infrastructure.OperationalRecords;
using SecureOps.Shared.Configuration;
using SecureOps.Shared.Contracts.InUse;

namespace SecureOps.Infrastructure.InUse.Execution;

/// <summary>Known mutation wire adapter. Not registered for dispatch until independent target/readback contracts are verified.</summary>
public sealed class TuruncuHatInUseMutationClient(IHttpClientFactory clients, ITuruncuHatSessionManager sessions,
    IOptions<TuruncuHatOptions> source, IOptions<OperationalRecordsOptions> writes, IOptions<InUseCompletionOptions> completion)
{
    /// <summary>One attempt only. Identities must come from a trusted, persisted target-validation step, never browser input.</summary>
    public async Task<InUseRemoteResult> SendAsync(string step, InUseExecutionLease lease, string? dynamicCaseId,
        string? activityId, string? environment, CancellationToken token)
    {
        if (!completion.Value.Enabled || completion.Value.Provider != "TuruncuHat" || writes.Value.ReadOnlyIntegrationMode
            || !writes.Value.ControlledTestWritesEnabled || !writes.Value.SourceCloseEnabled || lease.Intent.Source.Synthetic)
        { return new("Rejected", "WriteFence"); }
        if (lease.Step < 0 || lease.Step >= InUseExecutionWorker.Steps.Count || InUseExecutionWorker.Steps[lease.Step] != step
            || !lease.Evidence.Any(e => e.Step == "Validate" && e.Outcome == "Verified")
            || step == "Bpm" && !lease.Evidence.Any(e => e.Step == "Attachment" && e.Outcome == "Verified"))
        { return new("Rejected", "VerifiedPreconditionsRequired"); }
        if (!Uri.TryCreate(source.Value.BaseUrl.TrimEnd('/') + "/", UriKind.Absolute, out Uri? standard)
            || standard.Scheme != "https" || standard.UserInfo.Length != 0 || standard.Query.Length != 0 || standard.Fragment.Length != 0
            || !standard.AbsolutePath.EndsWith("/DataRest.svc/json/", StringComparison.Ordinal))
        { return new("Rejected", "UnsupportedSourceRoute"); }
        string session;
        try
        { session = await sessions.GetSessionAsync(token); }
        catch (Exception) { return new("Rejected", "SourceAuthenticationUnavailableBeforeMutation"); }
        HttpRequestMessage request;
        try
        {
            request = step switch
            {
                "Upload" => TuruncuHatInUseWireContract.Attachment(standard,
                    new Uri(standard, "../../DataRestSecure.svc/json/uploadattachment"), lease, session, source.Value.TenantId, source.Value.Authorization),
                "Property4463" => TuruncuHatInUseWireContract.Property(dynamicCaseId ?? "", 4463, "Application Server", session, source.Value.TenantId, source.Value.Authorization),
                "Property4464" when environment is not null && string.Equals(
                    InUseRequiredFields.Environment(lease.Intent.Source), environment, StringComparison.OrdinalIgnoreCase)
                    => TuruncuHatInUseWireContract.Property(dynamicCaseId ?? "", 4464, environment?.ToUpperInvariant() ?? "", session, source.Value.TenantId, source.Value.Authorization),
                "Bpm" => TuruncuHatInUseWireContract.Activity(activityId ?? "", lease.Intent.Source.Id, session, source.Value.TenantId, source.Value.Authorization),
                _ => throw new InvalidDataException("Unsupported or unverified mutation target.")
            };
            if (!request.RequestUri!.IsAbsoluteUri)
            { request.RequestUri = new Uri(standard, request.RequestUri); }
        }
        catch (InvalidDataException) { return new("Rejected", "VerifiedTargetRequired"); }
        using (request)
        using (HttpClient client = clients.CreateClient("TuruncuHat"))
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
        {
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(source.Value.RequestTimeoutSeconds, 1, 120)));
            try
            {
                using HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                { sessions.Invalidate(session); return new("Unknown", "AuthenticationRejectedReconcileBeforeRetry"); }
                if (!response.IsSuccessStatusCode)
                { return new("Unknown", "HttpOutcomeRequiresReconciliation"); }
                using JsonDocument json = await BoundedJsonHttpContent.ReadAsync(response.Content, source.Value.MaxResponseBytes, timeout.Token);
                return TuruncuHatInUseWireContract.Acknowledgement(json.RootElement, step == "Upload");
            }
            catch (Exception) { return new("Unknown", "ResponseUnavailableReconcileBeforeRetry"); }
        }
    }
}
