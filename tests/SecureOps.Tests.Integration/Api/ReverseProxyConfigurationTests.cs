using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using SecureOps.Api.Security;

namespace SecureOps.Tests.Integration.Api;

public sealed class ReverseProxyConfigurationTests
{
    [Fact]
    public void Validate_WhenEnabledWithoutTrustedProxies_Throws()
    {
        IConfiguration configuration = Build("true", []);

        Action act = () => ReverseProxyConfiguration.Validate(configuration);

        act.Should().Throw<InvalidOperationException>().WithMessage("*TrustedProxyIps is required*");
    }

    [Fact]
    public void Configure_UsesOnlyConfiguredExactProxyIp()
    {
        IConfiguration configuration = Build("true", ["10.0.0.9"]);
        ForwardedHeadersOptions options = new();

        ReverseProxyConfiguration.Validate(configuration);
        ReverseProxyConfiguration.Configure(options, configuration);

        options.KnownProxies.Should().ContainSingle().Which.ToString().Should().Be("10.0.0.9");
        options.ForwardedHeaders.Should().Be(ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto);
    }

    private static IConfiguration Build(string enabled, IReadOnlyCollection<string> ips)
    {
        Dictionary<string, string?> values = new() { ["ReverseProxy:ForwardedHeaders:Enabled"] = enabled };
        int index = 0;
        foreach (string ip in ips) { values[$"ReverseProxy:ForwardedHeaders:TrustedProxyIps:{index++}"] = ip; }
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }
}
