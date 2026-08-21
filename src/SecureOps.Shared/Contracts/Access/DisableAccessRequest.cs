namespace SecureOps.Shared.Contracts.Access;

/// <summary>Administrative request to disable application access.</summary>
public sealed record DisableAccessRequest(string Reason, long ExpectedVersion = 0);
