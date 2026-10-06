using Microsoft.AspNetCore.Mvc.Testing;
using SecureOps.Shared.Contracts.Api;

namespace SecureOps.Tests.Integration;

/// <summary>Explicit non-browser API transport used by existing hosted scenarios.</summary>
internal static class ApiTestClient
{
    public static HttpClient CreateApiClient(this WebApplicationFactory<Program> factory, WebApplicationFactoryClientOptions? options = null)
    {
        HttpClient client = factory.CreateClient(options ?? new WebApplicationFactoryClientOptions());
        client.DefaultRequestHeaders.Add(ApiCsrf.HeaderName, ApiCsrf.HeaderValue);
        return client;
    }
}
