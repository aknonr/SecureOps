namespace SecureOps.Shared.Contracts.Sessions;

/// <summary>Safe application-session metadata. No cookie, network, or device data is exposed.</summary>
public sealed record ApplicationSessionResponse(
    Guid SessionId,
    Guid UserId,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset LastSeenAtUtc,
    DateTimeOffset AbsoluteExpiresAtUtc,
    string AuthenticationMethod,
    long AccessVersion,
    string? Principal = null,
    string? NormalizedPrincipal = null,
    string? DisplayName = null,
    string? AuthenticationProvider = null,
    bool IsCurrent = false);

/// <summary>Bounded active-session administrative page.</summary>
public sealed record ActiveApplicationSessionsResponse(
    int Page,
    int PageSize,
    IReadOnlyList<ApplicationSessionResponse> Items);

/// <summary>Exact administrative application-session revocation request.</summary>
public sealed record RevokeApplicationSessionRequest(Guid SessionId, string Reason);

/// <summary>Safe application-session terminal response.</summary>
public sealed record ApplicationSessionEndedResponse(Guid SessionId, string EndReason, DateTimeOffset EndedAtUtc);
