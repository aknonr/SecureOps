using Microsoft.Extensions.Options;
using SecureOps.Shared.Configuration;

namespace SecureOps.Infrastructure.Identity;

/// <summary>
/// Mock identity directory provider for development and tests.
/// </summary>
public sealed class MockIdentityDirectoryProvider : IIdentityDirectoryProvider
{
    private readonly IReadOnlyDictionary<string, DirectoryUserRecord> _users;
    private readonly IdentityLookupOptions _options;

    /// <summary>
    /// Initializes a provider with a default sample user.
    /// </summary>
    public MockIdentityDirectoryProvider()
        : this(DefaultUsers(), Options.Create(new IdentityLookupOptions()))
    {
    }

    /// <summary>
    /// Initializes a provider with default users and configured options.
    /// </summary>
    /// <param name="options">Identity lookup options.</param>
    public MockIdentityDirectoryProvider(IOptions<IdentityLookupOptions> options)
        : this(DefaultUsers(), options)
    {
    }

    /// <summary>
    /// Initializes a provider with supplied users.
    /// </summary>
    /// <param name="users">Users keyed by their sAMAccountName.</param>
    public MockIdentityDirectoryProvider(IEnumerable<DirectoryUserRecord> users)
        : this(users, Options.Create(new IdentityLookupOptions()))
    {
    }

    /// <summary>
    /// Initializes a provider with supplied users and options.
    /// </summary>
    /// <param name="users">Users keyed by their sAMAccountName.</param>
    /// <param name="options">Identity lookup options.</param>
    public MockIdentityDirectoryProvider(IEnumerable<DirectoryUserRecord> users, IOptions<IdentityLookupOptions> options)
    {
        _users = users.ToDictionary(x => x.SamAccountName.ToLowerInvariant(), StringComparer.OrdinalIgnoreCase);
        _options = options.Value;
    }

    /// <inheritdoc />
    public Task<DirectoryUserRecord?> FindUserAsync(string normalizedAccount, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IdentityProviderInputGuard.EnsureSafeExactAccount(normalizedAccount, _options);
        _users.TryGetValue(normalizedAccount, out DirectoryUserRecord? user);
        return Task.FromResult(user);
    }

    private static IEnumerable<DirectoryUserRecord> DefaultUsers()
    {
        return
        [
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
        ];
    }
}
