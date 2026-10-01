namespace SecureOps.Domain.Access;

/// <summary>Persistence bounds for reviewed human-readable identity metadata.</summary>
public static class ApplicationUserProfileLimits
{
    /// <summary>Maximum persisted corporate login-name length.</summary>
    public const int LoginName = 256;

    /// <summary>Maximum persisted display-name length.</summary>
    public const int DisplayName = 256;

    /// <summary>Maximum persisted mail-address length.</summary>
    public const int Mail = 320;

    /// <summary>Maximum persisted corporate UID length.</summary>
    public const int Uid = 256;
}
