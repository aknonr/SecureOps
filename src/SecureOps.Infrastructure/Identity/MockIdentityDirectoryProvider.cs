namespace SecureOps.Infrastructure.Identity;

/// <summary>
/// Mock identity directory provider for development and tests.
/// </summary>
public sealed class MockIdentityDirectoryProvider : IIdentityDirectoryProvider
{
    private readonly IReadOnlyDictionary<string, DirectoryUserRecord> _users;

    /// <summary>
    /// Initializes a provider with a default sample user.
    /// </summary>
    public MockIdentityDirectoryProvider()
        : this(new[]
        {
            new DirectoryUserRecord(
                "Example Admin",
                "pam12356",
                "pam12356@contoso.local",
                "example.admin@contoso.local",
                "Windows Operations",
                "Systems Engineer",
                "Example Manager",
                true,
                false,
                "Mock")
        })
    {
    }

    /// <summary>
    /// Initializes a provider with supplied users.
    /// </summary>
    /// <param name="users">Users keyed by their sAMAccountName.</param>
    public MockIdentityDirectoryProvider(IEnumerable<DirectoryUserRecord> users)
    {
        _users = users.ToDictionary(x => x.SamAccountName.ToLowerInvariant(), StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public Task<DirectoryUserRecord?> FindUserAsync(string normalizedAccount, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _users.TryGetValue(normalizedAccount, out DirectoryUserRecord? user);
        return Task.FromResult(user);
    }
}
