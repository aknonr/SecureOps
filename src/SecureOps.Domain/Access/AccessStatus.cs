namespace SecureOps.Domain.Access;

/// <summary>Application access state independent of the authentication provider.</summary>
public enum AccessStatus
{
    /// <summary>The corporate principal is known but has no operational permissions.</summary>
    Pending,
    /// <summary>An authorized administrator approved application access.</summary>
    Approved,
    /// <summary>Application access was explicitly disabled.</summary>
    Disabled
}
