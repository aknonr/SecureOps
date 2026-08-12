using System.Text.Json.Serialization;

namespace SecureOps.Ui.Services;

/// <summary>
/// Wire shape of an API error body.
/// </summary>
/// <remarks>
/// The API emits RFC 7807 <c>application/problem+json</c> with the safe extensions documented in
/// <c>docs/contracts/secureops-api-v1-ui-integration.md</c>: <c>code</c>, <c>stage</c>,
/// <c>retryable</c>, <c>correlationId</c>, and <c>traceId</c>.
/// <para>
/// The legacy <c>ApiErrorResponse</c> members (<c>errorCode</c>, <c>message</c>) are also bound so a
/// single reader covers both shapes. That keeps the UI working against endpoints that have not been
/// migrated yet without a second parsing path; it is deliberately tolerant, not authoritative.
/// </para>
/// </remarks>
public sealed class ProblemDetailsPayload
{
    /// <summary>RFC 7807 problem type URI.</summary>
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    /// <summary>RFC 7807 short title.</summary>
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    /// <summary>RFC 7807 status code.</summary>
    [JsonPropertyName("status")]
    public int? Status { get; set; }

    /// <summary>RFC 7807 detail text.</summary>
    [JsonPropertyName("detail")]
    public string? Detail { get; set; }

    /// <summary>Stable SecureOps error code extension.</summary>
    [JsonPropertyName("code")]
    public string? Code { get; set; }

    /// <summary>Safe pipeline stage extension.</summary>
    [JsonPropertyName("stage")]
    public string? Stage { get; set; }

    /// <summary>Whether the API considers the operation retryable.</summary>
    [JsonPropertyName("retryable")]
    public bool? Retryable { get; set; }

    /// <summary>Support correlation identifier extension.</summary>
    [JsonPropertyName("correlationId")]
    public string? CorrelationId { get; set; }

    /// <summary>Trace identifier extension.</summary>
    [JsonPropertyName("traceId")]
    public string? TraceId { get; set; }

    /// <summary>Legacy <c>ApiErrorResponse.ErrorCode</c>.</summary>
    [JsonPropertyName("errorCode")]
    public string? ErrorCode { get; set; }

    /// <summary>Legacy <c>ApiErrorResponse.Message</c>.</summary>
    [JsonPropertyName("message")]
    public string? Message { get; set; }

    /// <summary>
    /// Effective error code from either the ProblemDetails extension or the legacy field.
    /// </summary>
    public string? EffectiveCode => FirstNonBlank(Code, ErrorCode);

    /// <summary>
    /// Effective support reference from either the ProblemDetails extensions or the legacy field.
    /// </summary>
    public string? EffectiveCorrelationId => FirstNonBlank(CorrelationId, TraceId);

    private static string? FirstNonBlank(params string?[] candidates) =>
        candidates.FirstOrDefault(candidate => !string.IsNullOrWhiteSpace(candidate));
}
