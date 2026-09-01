using System.Net.Http.Headers;
using Microsoft.Extensions.Options;
using SecureOps.Shared.Configuration;

namespace SecureOps.Ui.Services;

/// <summary>Relays the current browser session's server-held OIDC access token to the API.</summary>
public sealed class OidcApiAccessTokenHandler : DelegatingHandler
{
    private readonly IApiSessionStore _sessions;
    private readonly OidcOptions _options;
    private readonly TimeProvider _timeProvider;

    /// <summary>Initializes the token relay.</summary>
    public OidcApiAccessTokenHandler(
        IApiSessionStore sessions,
        IOptions<OidcOptions> options,
        TimeProvider timeProvider)
    {
        _sessions = sessions;
        _options = options.Value;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (_options.Enabled)
        {
            string? browserSessionKey = ApiSessionHeaders.ReadBrowserSessionKey(request);
            if (browserSessionKey is not null)
            {
                string? token = _sessions.GetOrCreate(browserSessionKey).GetOidcAccessToken(_timeProvider.GetUtcNow());
                if (token is not null)
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                }
            }
        }

        return base.SendAsync(request, cancellationToken);
    }
}
