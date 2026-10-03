using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using SecureOps.Shared.Auth;
using SecureOps.Shared.Configuration;

namespace SecureOps.Ui.Services;

/// <summary>Bounded server-only OIDC token and UserInfo transport.</summary>
public interface IOidcBackchannelClient
{
    /// <summary>Posts one token request using the configured provider wire format.</summary>
    public Task<OpenIdConnectMessage> PostTokenAsync(
        Uri endpoint,
        IReadOnlyDictionary<string, string> parameters,
        CancellationToken cancellationToken);

    /// <summary>Gets one bounded UserInfo document with a server-held access token.</summary>
    public Task<JsonDocument> GetUserInfoAsync(Uri endpoint, string accessToken, CancellationToken cancellationToken);
}

/// <inheritdoc />
/// <summary>Initializes the server-only backchannel.</summary>
public sealed class OidcBackchannelClient(
    IOptions<OidcOptions> configured,
    IOptionsMonitor<OpenIdConnectOptions> handlerOptions) : IOidcBackchannelClient
{
    private readonly OidcOptions _configured = configured.Value;
    private readonly IOptionsMonitor<OpenIdConnectOptions> _handlerOptions = handlerOptions;

    /// <inheritdoc />
    public async Task<OpenIdConnectMessage> PostTokenAsync(
        Uri endpoint,
        IReadOnlyDictionary<string, string> parameters,
        CancellationToken cancellationToken)
    {
        ValidateEndpoint(endpoint);
        using HttpRequestMessage request = new(HttpMethod.Post, endpoint);
        request.Content = string.Equals(_configured.TokenEndpointRequestFormat, "Json", StringComparison.OrdinalIgnoreCase)
            ? JsonContent.Create(parameters)
            : new FormUrlEncodedContent(parameters);
        using CancellationTokenSource timeout = CreateTimeout(cancellationToken);
        using HttpResponseMessage response = await SendAsync(request, timeout.Token);
        byte[] body = await ReadBoundedAsync(response, timeout.Token);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException("The OIDC token endpoint rejected the server-side request.");
        }

        return new OpenIdConnectMessage(System.Text.Encoding.UTF8.GetString(body));
    }

    /// <inheritdoc />
    public async Task<JsonDocument> GetUserInfoAsync(
        Uri endpoint,
        string accessToken,
        CancellationToken cancellationToken)
    {
        ValidateEndpoint(endpoint);
        using HttpRequestMessage request = new(HttpMethod.Get, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using CancellationTokenSource timeout = CreateTimeout(cancellationToken);
        using HttpResponseMessage response = await SendAsync(request, timeout.Token);
        byte[] body = await ReadBoundedAsync(response, timeout.Token);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException("The OIDC UserInfo endpoint rejected the server-side request.");
        }

        return JsonDocument.Parse(body, new JsonDocumentOptions { MaxDepth = 16 });
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        OpenIdConnectOptions options = _handlerOptions.Get(ExternalIdentityClaimTypes.OidcInteractiveScheme);
        return await options.Backchannel.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }

    private CancellationTokenSource CreateTimeout(CancellationToken cancellationToken)
    {
        var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(_configured.BackchannelTimeoutSeconds));
        return timeout;
    }

    private async Task<byte[]> ReadBoundedAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength > _configured.MaxBackchannelResponseBytes)
        {
            throw new InvalidOperationException("The OIDC backchannel response exceeded the configured limit.");
        }

        await using Stream source = await response.Content.ReadAsStreamAsync(cancellationToken);
        using MemoryStream destination = new(capacity: Math.Min(_configured.MaxBackchannelResponseBytes, 16_384));
        byte[] buffer = new byte[8192];
        while (true)
        {
            int read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                return destination.ToArray();
            }

            if (destination.Length + read > _configured.MaxBackchannelResponseBytes)
            {
                throw new InvalidOperationException("The OIDC backchannel response exceeded the configured limit.");
            }

            destination.Write(buffer, 0, read);
        }
    }

    private static void ValidateEndpoint(Uri endpoint)
    {
        if (!endpoint.IsAbsoluteUri
            || !string.Equals(endpoint.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrEmpty(endpoint.UserInfo))
        {
            throw new InvalidOperationException("OIDC backchannel endpoints must be absolute HTTPS URLs.");
        }
    }
}
