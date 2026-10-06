using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SecureOps.Shared.Contracts.Api;
using SecureOps.Ui.Hosting;

namespace SecureOps.Tests.Integration.Ui;

public sealed class UiApiCsrfTransportTests
{
    [Fact]
    public void SharedTransport_AllRegisteredApiClients_SupplyIntentWithoutBrowserMetadata()
    {
        using UiFactory factory = new();
        using HttpClient browser = factory.CreateClient();
        IHttpClientFactory clients = factory.Services.GetRequiredService<IHttpClientFactory>();
        // Typed clients use their interface/class names as their factory names.
        string[] names = ["IIdentityLookupApiClient", "IAccessApiClient", "IAccessAdminApiClient", "IOperationalRecordApiClient",
            "IManagementReportingApiClient", "IDirectoryApiClient", "ISessionApiClient", "IResourceApiClient", "InUseApiClient",
            "OperationsDiagnosticsApiClient", "AnnouncementApiClient", "OperationHistoryApiClient", "ServiceAccountApiClient"];
        foreach (string name in names)
        {
            using HttpClient api = clients.CreateClient(name);
            api.DefaultRequestHeaders.GetValues(ApiCsrf.HeaderName).Should().Equal([ApiCsrf.HeaderValue], name);
            api.DefaultRequestHeaders.Contains("Origin").Should().BeFalse();
            api.DefaultRequestHeaders.Contains("Sec-Fetch-Site").Should().BeFalse();
            api.BaseAddress.Should().Be(new Uri("http://localhost:51731/"));
        }
    }

    private sealed class UiFactory : WebApplicationFactory<HttpsOffloadOptions>
    {
        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.UseEnvironment("Demo");
            builder.ConfigureHostConfiguration(configuration => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DemoMode:Enabled"] = "true",
                ["DemoMode:AllowMockAuthentication"] = "true",
                ["IdentityLookupApi:BaseAddress"] = "http://localhost:51731/",
                ["ReverseProxy:HttpsOffload:Enabled"] = "false"
            }));
            return base.CreateHost(builder);
        }
    }
}
