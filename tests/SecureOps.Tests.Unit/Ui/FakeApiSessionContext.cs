using SecureOps.Ui.Services;

namespace SecureOps.Tests.Unit.Ui;

/// <summary>
/// Test <see cref="IApiSessionContext"/> with a fixed browser-session correlation value.
/// </summary>
/// <remarks>
/// A null key is the realistic unauthenticated case, so it is the default here: client tests that
/// say nothing about sessions should exercise the path that adds no correlation stamp.
/// </remarks>
internal sealed class FakeApiSessionContext : IApiSessionContext
{
    /// <summary>Initializes a context with an optional correlation value.</summary>
    /// <param name="browserSessionKey">Correlation value, or null for none.</param>
    public FakeApiSessionContext(string? browserSessionKey = null)
    {
        BrowserSessionKey = browserSessionKey;
    }

    /// <inheritdoc />
    public string? BrowserSessionKey { get; private set; }

    /// <inheritdoc />
    public void Seed(string? key)
    {
        if (!string.IsNullOrWhiteSpace(key))
        {
            BrowserSessionKey = key;
        }
    }
}
