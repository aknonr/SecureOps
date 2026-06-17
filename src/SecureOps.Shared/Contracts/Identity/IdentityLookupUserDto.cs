namespace SecureOps.Shared.Contracts.Identity;

/// <summary>
/// Approved operational identity fields returned by identity lookup.
/// </summary>
/// <param name="DisplayName">Display name from AD.</param>
/// <param name="SamAccountName">sAMAccountName from AD.</param>
/// <param name="UserPrincipalName">User principal name from AD.</param>
/// <param name="Mail">Mail address from AD.</param>
/// <param name="Department">Department from AD.</param>
/// <param name="Title">Title from AD.</param>
/// <param name="ManagerDisplayName">Manager display name when resolvable.</param>
/// <param name="Enabled">Whether the account is enabled when known.</param>
/// <param name="Locked">Whether the account is locked when known.</param>
public sealed record IdentityLookupUserDto(
    string? DisplayName,
    string SamAccountName,
    string? UserPrincipalName,
    string? Mail,
    string? Department,
    string? Title,
    string? ManagerDisplayName,
    bool? Enabled,
    bool? Locked);
