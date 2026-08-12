namespace SecureOps.Shared.Contracts.Access;

/// <summary>Administrative access-request decision.</summary>
public sealed record AccessDecisionRequest(string Reason, IReadOnlyList<string>? Roles);
