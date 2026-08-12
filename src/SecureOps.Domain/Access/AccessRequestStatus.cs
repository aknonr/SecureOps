namespace SecureOps.Domain.Access;

/// <summary>Durable access-request decision state.</summary>
public enum AccessRequestStatus
{
    /// <summary>The request is awaiting an authorized decision.</summary>
    Pending,
    /// <summary>The request was approved.</summary>
    Approved,
    /// <summary>The request was rejected.</summary>
    Rejected
}
