namespace SecureOps.Infrastructure.Access;

/// <summary>Authenticated actor context for access operations and audit.</summary>
public sealed record AccessOperationContext(string Actor, string CorrelationId, string? SourceIp);
