namespace SecureOps.Infrastructure.Identity;

/// <summary>
/// Approved identity fields read from a directory provider.
/// </summary>
/// <param name="DisplayName">Display name.</param>
/// <param name="SamAccountName">sAMAccountName.</param>
/// <param name="UserPrincipalName">User principal name.</param>
/// <param name="Mail">Mail address.</param>
/// <param name="Department">Department.</param>
/// <param name="Title">Title.</param>
/// <param name="ManagerDisplayName">Manager display name.</param>
/// <param name="Enabled">Enabled state when known.</param>
/// <param name="Locked">Locked state when known.</param>
/// <param name="Source">Source provider name.</param>
/// <param name="AccountTypeEvidence">Directory object class evidence, never inferred from the account name.</param>
public sealed record DirectoryUserRecord(
    string? DisplayName,
    string SamAccountName,
    string? UserPrincipalName,
    string? Mail,
    string? Department,
    string? Title,
    string? ManagerDisplayName,
    bool? Enabled,
    bool? Locked,
    string Source,
    string AccountTypeEvidence = "User");
