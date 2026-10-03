using System.Net.Http.Headers;
using Microsoft.Extensions.Options;
using SecureOps.Shared.Configuration;

namespace SecureOps.Ui.Services;

/// <summary>Relays the current browser session's server-held OIDC access token to the API.</summary>
public sealed class OidcApiAccessTokenHandler(
    IApiSessionStore sessions,
    IOptions<OidcOptions> options,
    TimeProvider timeProvider,
    IOidcBackchannelClient backchannel) : DelegatingHandler
{
    private readonly IApiSessionStore _sessions = sessions;
    private readonly OidcOptions _options = options.Value;
    private readonly TimeProvider _timeProvider = timeProvider;
    private readonly IOidcBackchannelClient _backchannel = backchannel;

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (_options.Enabled)
        {
            string? browserSessionKey = ApiSessionHeaders.ReadBrowserSessionKey(request);
            if (browserSessionKey is not null)
            {
                OidcAccessTokenResult result = await _sessions.GetOrCreate(browserSessionKey)
                    .GetOidcAccessTokenAsync(_timeProvider.GetUtcNow(), _options, _backchannel, cancellationToken);
                if (result.AccessToken is not null)
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", result.AccessToken);
                }
            }
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
