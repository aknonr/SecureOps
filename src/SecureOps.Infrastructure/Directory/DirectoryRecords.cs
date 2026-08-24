namespace SecureOps.Infrastructure.DirectoryExplorer;

/// <summary>Provider-neutral safe directory group record.</summary>
public sealed record DirectoryGroupRecord(
    string? StableIdentifier,
    string? Name,
    string? SamAccountName,
    string? DistinguishedName,
    string? Description,
    string Category,
    string Scope,
    string? ManagedBy = null,
    int? DirectMemberCount = null,
    string? ManagedByDisplayName = null,
    DateTimeOffset? CreatedAtUtc = null,
    DateTimeOffset? ChangedAtUtc = null,
    string? MembershipKind = null);

/// <summary>Provider-neutral safe direct member record.</summary>
public sealed record DirectoryMemberRecord(
    string? StableIdentifier,
    string? Name,
    string? SamAccountName,
    string? DistinguishedName,
    string MemberType);

/// <summary>Provider-neutral exact account-health and service evidence.</summary>
public sealed record DirectoryPrincipalEnrichmentRecord(
    string? StableIdentifier,
    string? DisplayName,
    string? SamAccountName,
    string? UserPrincipalName,
    bool? Enabled,
    bool? Locked,
    DateTimeOffset? PasswordLastSetUtc,
    bool? PasswordNeverExpires,
    DateTimeOffset? AccountExpiresUtc,
    bool? MustChangePassword,
    DateTimeOffset? LastLogonTimestampUtc,
    string? ManagedBy,
    IReadOnlyList<string> ServicePrincipalNames,
    int ServicePrincipalNameCount,
    bool ServicePrincipalNamesTruncated,
    string AccountTypeEvidence);

/// <summary>One bounded provider page.</summary>
public sealed record DirectoryProviderPage<T>(IReadOnlyList<T> Items, bool HasMore, bool IsPartial = false);

/// <summary>Safe provider result-limit failure.</summary>
public sealed class DirectoryQueryLimitExceededException : Exception
{
    /// <summary>Initializes the safe failure.</summary>
    public DirectoryQueryLimitExceededException() : base("DirectoryQueryLimitExceeded") { }
}

/// <summary>Safe provider availability failure.</summary>
public sealed class DirectoryProviderUnavailableException : Exception
{
    /// <summary>Initializes the safe failure.</summary>
    public DirectoryProviderUnavailableException(Exception? innerException = null)
        : base("DirectoryProviderUnavailable", innerException) { }
}
