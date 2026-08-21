namespace SecureOps.Shared.Contracts.Access;

/// <summary>Administrative replacement of active application roles.</summary>
public sealed record AssignRolesRequest(IReadOnlyList<string> Roles, string Reason, long ExpectedVersion = 0);
