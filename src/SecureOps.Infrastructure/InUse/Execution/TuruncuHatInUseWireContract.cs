using System.Net.Http.Json;
using System.Text.Json;
using SecureOps.Infrastructure.OperationalRecords;
using SecureOps.Shared.Contracts.InUse;

namespace SecureOps.Infrastructure.InUse.Execution;

/// <summary>Known legacy request shapes only. Dispatch remains blocked until target/readback contracts are independently verified.</summary>
public static class TuruncuHatInUseWireContract
{
    /// <summary>Builds an exact attachment request using the reviewed bytes, never a regenerated workbook.</summary>
    public static HttpRequestMessage Attachment(Uri standardEndpoint, Uri attachmentEndpoint, InUseExecutionLease lease,
        string session, int tenant, string authorization)
    {
        if (standardEndpoint.Scheme != "https" || attachmentEndpoint.Scheme != "https"
            || standardEndpoint.Authority != attachmentEndpoint.Authority || !string.IsNullOrEmpty(attachmentEndpoint.UserInfo)
            || !attachmentEndpoint.AbsolutePath.EndsWith("/DataRestSecure.svc/json/uploadattachment", StringComparison.Ordinal)
            || attachmentEndpoint.Query.Length != 0 || attachmentEndpoint.Fragment.Length != 0
            || !InUseAspectParser.NumericId(lease.Intent.Source.Id) || tenant <= 0
            || Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(lease.Artifact)) != lease.Intent.ReportSha256
            || !System.Text.RegularExpressions.Regex.IsMatch(lease.Intent.Source.Code, @"\AOR-[0-9]{1,30}\z",
                System.Text.RegularExpressions.RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
        { throw new InvalidDataException("Exact same-origin attachment target and artifact required."); }
        return Request(attachmentEndpoint, new
        {
            fBase = "SMSS_oRFF",
            fId = lease.Intent.Source.Id,
            fName = lease.Intent.Source.Code + "_InUse.xlsx",
            datastring = Convert.ToBase64String(lease.Artifact),
            SessionID = session,
            TenantId = tenant
        }, authorization);
    }
    /// <summary>Caller must supply a verified dynamic-case identity and reviewed homogeneous environment, not a positional value.</summary>
    public static HttpRequestMessage Property(string dynamicCaseId, int property, string value, string session, int tenant, string authorization)
    {
        if (!InUseAspectParser.NumericId(dynamicCaseId) || property is not (4463 or 4464)
            || property == 4463 && value != "Application Server"
            || property == 4464 && value is not ("PROD" or "DEV" or "TEST" or "NONPROD") || tenant <= 0)
        { throw new InvalidDataException("Verified property target and value required."); }
        return Request(new Uri("update", UriKind.Relative), new
        {
            BaseObject = "DCM_DynamicCaseProperty",
            Filters = new[] { $"#%p_dc%#={dynamicCaseId} AND #%p_dcctp%#={property}" },
            Updates = new[] { "p_value", value },
            SessionID = session,
            TenantId = tenant
        }, authorization);
    }
    /// <summary>Rechecks eligibility in the mutation predicate; never updates an arbitrary first result.</summary>
    public static HttpRequestMessage Activity(string activityId, string sourceId, string session, int tenant, string authorization)
    {
        if (!InUseAspectParser.NumericId(activityId) || !InUseAspectParser.NumericId(sourceId) || tenant <= 0)
        { throw new InvalidDataException("Verified unique activity and OR identities required."); }
        return Request(new Uri("update", UriKind.Relative), new
        {
            BaseObject = "BPM_Actvty",
            Filters = new[] { $"#%id%#={activityId} AND (#%m_actvty_task_model%#=103626 OR #%m_actvty_task_model%#=103627) AND #%m_status%#=1 AND #%m_group%#=68 AND #%m_process.m_main_object_id%#={sourceId}" },
            Updates = new[] { "m_status", "4" },
            SessionID = session,
            TenantId = tenant
        }, authorization);
    }
    /// <summary>An explicit acknowledgement is not attachment readback or OR closure. Missing success remains unknown.</summary>
    public static InUseRemoteResult Acknowledgement(JsonElement response, bool attachment)
    {
        string root = attachment ? "UploadAttachmentStringResult" : "UpdateResult";
        if (response.ValueKind != JsonValueKind.Object || !response.TryGetProperty(root, out JsonElement result) || result.ValueKind != JsonValueKind.Object)
        { return new("Unknown", "UnsupportedAcknowledgement"); }
        if (result.TryGetProperty("Success", out JsonElement rejected) && rejected.ValueKind == JsonValueKind.False)
        { return new("Rejected", "SourceExplicitRejection"); }
        if (result.TryGetProperty("ErrorNo", out JsonElement number) && number.ValueKind != JsonValueKind.Null)
        {
            string? text = number.ValueKind == JsonValueKind.String ? number.GetString()
                : number.ValueKind == JsonValueKind.Number ? number.GetRawText() : null;
            if (!int.TryParse(text, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out int errorNo) || errorNo != 0)
            { return new("Unknown", "SourceReportedFailureReconcileBeforeRetry"); }
        }
        foreach (string field in new[] { "ErrorDescription", "ErrorDetails" })
        {
            if (result.TryGetProperty(field, out JsonElement error) && error.ValueKind != JsonValueKind.Null
                && (error.ValueKind != JsonValueKind.String || !string.IsNullOrWhiteSpace(error.GetString())))
            { return new("Unknown", "SourceReportedFailureReconcileBeforeRetry"); }
        }
        return result.TryGetProperty("Success", out JsonElement success) && success.ValueKind == JsonValueKind.True
            ? new("Acknowledged", "SourceAcknowledgementOnly") : new("Unknown", "SuccessNotExplicit");
    }
    private static HttpRequestMessage Request<T>(Uri uri, T body, string authorization)
    {
        HttpRequestMessage request = new(HttpMethod.Post, uri) { Content = JsonContent.Create(new { req = body }, options: LegacyContractJson.Options) };
        TuruncuHatSessionManager.ApplyHeaders(request, authorization);
        return request;
    }
}
