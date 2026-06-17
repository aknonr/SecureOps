namespace SecureOps.Shared.Contracts.Identity;

/// <summary>
/// Request for exact privileged account identity lookup.
/// </summary>
/// <param name="Account">Exact PAM account or AD username.</param>
/// <param name="Purpose">Incident-response context for the privileged read.</param>
/// <param name="AlertId">Optional related SecureOps alert ID.</param>
/// <param name="TuruncuhatEvtId">Optional related Turuncuhat EVT ID.</param>
public sealed record IdentityLookupRequest(
    string Account,
    string Purpose,
    Guid? AlertId = null,
    string? TuruncuhatEvtId = null);
