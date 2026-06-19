namespace SecureOps.Shared.Contracts.Api;

/// <summary>
/// Standard safe API error response.
/// </summary>
/// <param name="ErrorCode">Machine-readable error code.</param>
/// <param name="Message">Safe user-facing message.</param>
/// <param name="CorrelationId">Request correlation ID for support and audit correlation.</param>
public sealed record ApiErrorResponse(
    string ErrorCode,
    string Message,
    string? CorrelationId);
