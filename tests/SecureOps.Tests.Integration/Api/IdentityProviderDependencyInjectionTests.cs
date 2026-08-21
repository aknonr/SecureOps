using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SecureOps.Infrastructure;
using SecureOps.Infrastructure.Identity;
using SecureOps.Shared.Contracts.Api;
using SecureOps.Shared.Contracts.Identity;

namespace SecureOps.Tests.Integration.Api;

public sealed class IdentityProviderDependencyInjectionTests
{
    [Fact]
    public async Task AddSecureOpsInfrastructure_WithMockProvider_UsesDeterministicFactory()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["IdentityLookup:Provider"] = "Mock"
            })
            .Build();
        ServiceCollection services = new();

        services.AddSecureOpsInfrastructure(configuration);

        ServiceDescriptor registration = services.Single(descriptor =>
            descriptor.ServiceType == typeof(IIdentityDirectoryProvider));
        registration.ImplementationFactory.Should().NotBeNull();
        registration.ImplementationType.Should().BeNull();

        await using ServiceProvider serviceProvider = services.BuildServiceProvider();
        IIdentityDirectoryProvider provider = serviceProvider.GetRequiredService<IIdentityDirectoryProvider>();
        DirectoryUserRecord? user = await provider.FindUserAsync("pam12356", CancellationToken.None);

        user.Should().NotBeNull();
        user!.SamAccountName.Should().Be("pam12356");
    }

    [Fact]
    public void AddSecureOpsInfrastructure_WithActiveDirectoryProvider_UsesSingleConstructorChain()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["IdentityLookup:Provider"] = "ActiveDirectory",
                ["IdentityLookup:DomainName"] = "example.invalid"
            })
            .Build();
        ServiceCollection services = new();

        services.AddSecureOpsInfrastructure(configuration);

        ServiceDescriptor providerRegistration = services.Single(descriptor =>
            descriptor.ServiceType == typeof(IIdentityDirectoryProvider));
        ServiceDescriptor clientRegistration = services.Single(descriptor =>
            descriptor.ServiceType == typeof(IActiveDirectoryLookupClient));
        providerRegistration.ImplementationType.Should().Be(typeof(ActiveDirectoryIdentityDirectoryProvider));
        clientRegistration.ImplementationType.Should().Be(typeof(ActiveDirectoryLookupClient));
        typeof(ActiveDirectoryIdentityDirectoryProvider).GetConstructors().Should().ContainSingle();
        typeof(ActiveDirectoryLookupClient).GetConstructors().Should().ContainSingle();
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Demo")]
    [InlineData("Test")]
    public async Task Lookup_WithMockProvider_ResolvesSeededPamAccountInAllowedEnvironment(string environment)
    {
        using WebApplicationFactory<Program> factory = CreateFactory(environment);
        using HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-SecureOps-Demo-Actor", "platform-admin");

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/identity/lookup",
            new { account = "pam12356", purpose = "Approved operational lookup" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        IdentityLookupResponse? result = await response.Content.ReadFromJsonAsync<IdentityLookupResponse>();
        result!.Status.Should().Be("Found");
        result.User!.SamAccountName.Should().Be("pam12356");
    }

    [Fact]
    public async Task Capabilities_WithMockProvider_AdvertisesWorkingExactUpnLookup()
    {
        using WebApplicationFactory<Program> factory = CreateFactory("Demo", enableUpnLookup: true);
        using HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-SecureOps-Demo-Actor", "platform-admin");

        IdentityLookupCapabilitiesResponse? capabilities = await client.GetFromJsonAsync<IdentityLookupCapabilitiesResponse>(
            "/api/v1/identity/lookup/capabilities");
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/identity/lookup",
            new { account = "pam12356@contoso.local", purpose = "Approved operational lookup" });

        capabilities!.SupportsUpnLookup.Should().BeTrue();
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        IdentityLookupResponse? result = await response.Content.ReadFromJsonAsync<IdentityLookupResponse>();
        result!.User!.SamAccountName.Should().Be("pam12356");
    }

    [Fact]
    public async Task Capabilities_WithMockProviderAndUpnDisabled_DoesNotAdvertiseUpnLookup()
    {
        using WebApplicationFactory<Program> factory = CreateFactory("Demo", enableUpnLookup: false);
        using HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-SecureOps-Demo-Actor", "platform-admin");

        IdentityLookupCapabilitiesResponse? capabilities = await client.GetFromJsonAsync<IdentityLookupCapabilitiesResponse>(
            "/api/v1/identity/lookup/capabilities");

        capabilities!.SupportsUpnLookup.Should().BeFalse();
    }

    [Fact]
    public async Task Lookup_WithUnknownExactUpn_ReturnsLegitimateNotFound()
    {
        using WebApplicationFactory<Program> factory = CreateFactory("Demo");
        using HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-SecureOps-Demo-Actor", "platform-admin");

        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/v1/identity/lookup",
            new { account = "missing.account@contoso.local", purpose = "Approved operational lookup" });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var problem = JsonNode.Parse(await response.Content.ReadAsStringAsync());
        problem!["code"]!.GetValue<string>().Should().Be(OperationalErrorCodes.IdentityNotFound);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Capabilities_WithActiveDirectoryProvider_ReportsEffectiveUpnSetting(bool enableUpnLookup)
    {
        using WebApplicationFactory<Program> factory = CreateFactory(
            "Demo",
            identityProvider: "ActiveDirectory",
            enableUpnLookup: enableUpnLookup);
        using HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-SecureOps-Demo-Actor", "platform-admin");

        IdentityLookupCapabilitiesResponse? capabilities = await client.GetFromJsonAsync<IdentityLookupCapabilitiesResponse>(
            "/api/v1/identity/lookup/capabilities");

        capabilities!.SupportsUpnLookup.Should().Be(enableUpnLookup);
    }

    private static WebApplicationFactory<Program> CreateFactory(
        string environment,
        string identityProvider = "Mock",
        bool enableUpnLookup = true)
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment(environment);
                builder.UseSetting("DemoAuth:Enabled", "true");
                builder.UseSetting("DemoAuth:HeaderName", "X-SecureOps-Demo-Actor");
                builder.UseSetting("Access:DemoCompatibilityEnabled", "true");
                builder.UseSetting("Audit:Provider", "InMemory");
                builder.UseSetting("IdentityLookup:Provider", identityProvider);
                builder.UseSetting("IdentityLookup:DomainName", "example.invalid");
                builder.UseSetting("IdentityLookup:EnableUpnLookup", enableUpnLookup ? "true" : "false");
                builder.UseSetting("RateLimiting:IdentityLookup:PermitLimit", "100");
            });
    }
}
