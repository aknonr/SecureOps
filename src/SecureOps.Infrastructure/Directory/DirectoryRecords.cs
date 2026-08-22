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
    int? DirectMemberCount = null);

/// <summary>Provider-neutral safe direct member record.</summary>
public sealed record DirectoryMemberRecord(
    string? StableIdentifier,
    string? Name,
    string? SamAccountName,
    string? DistinguishedName,
    string MemberType);

/// <summary>One bounded provider page.</summary>
public sealed record DirectoryProviderPage<T>(IReadOnlyList<T> Items, bool HasMore);

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
