using Microsoft.AspNetCore.Authorization;

namespace SecureOps.Api.Security;

/// <summary>Requires approved application access with one explicit capability.</summary>
public sealed class CapabilityRequirement : IAuthorizationRequirement
{
    /// <summary>Initializes the requirement.</summary>
    public CapabilityRequirement(string capability)
    {
        Capability = capability;
    }

    /// <summary>Required capability identifier.</summary>
    public string Capability { get; }
}
