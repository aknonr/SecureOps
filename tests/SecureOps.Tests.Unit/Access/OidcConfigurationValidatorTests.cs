using FluentAssertions;
using Microsoft.Extensions.Configuration;
using SecureOps.Infrastructure.Access;

namespace SecureOps.Tests.Unit.Access;

public sealed class OidcConfigurationValidatorTests
{
    [Fact]
    public void DisabledOidc_AllowsIncompleteServerConfiguration()
    {
        IConfiguration configuration = Configuration(new() { ["Oidc:Enabled"] = "false" });

        Action act = () => OidcConfigurationValidator.Validate(configuration, "Production");

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("Oidc:Authority")]
    [InlineData("Oidc:ClientId")]
    [InlineData("Oidc:ApiAudience")]
    public void EnabledOidc_RequiresRuntimeIdentityContract(string missingKey)
    {
        Dictionary<string, string?> values = Valid();
        values[missingKey] = string.Empty;

        Action act = () => OidcConfigurationValidator.Validate(Configuration(values), "Test");

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void PublicClient_DoesNotRequireASecret()
    {
        Dictionary<string, string?> values = Valid();
        values["Oidc:ClientAuthenticationMethod"] = "None";

        Action act = () => OidcConfigurationValidator.Validate(Configuration(values), "Test");

        act.Should().NotThrow();
    }

    [Fact]
    public void ConfidentialClient_RequiresServerOwnedSecret()
    {
        Dictionary<string, string?> values = Valid();
        values["Oidc:ClientAuthenticationMethod"] = "ClientSecretPost";

        Action act = () => OidcConfigurationValidator.Validate(Configuration(values), "Test");

        act.Should().Throw<InvalidOperationException>().WithMessage("*ClientSecret*");
    }

    [Fact]
    public void ApiResourceServer_DoesNotRequireUiClientCredential()
    {
        IConfiguration configuration = Configuration(new()
        {
            ["Oidc:Enabled"] = "true",
            ["Oidc:Authority"] = "https://identity.example.test",
            ["Oidc:ApiAudience"] = "secureops-api-test"
        });

        Action act = () => OidcConfigurationValidator.ValidateApi(configuration, "Production");

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("Oidc:UsePkce", "false")]
    [InlineData("Oidc:Scopes:0", "profile")]
    [InlineData("Oidc:CallbackPath", "https://example.test/signin-oidc")]
    [InlineData("Oidc:Authority", "http://identity.example.test")]
    public void EnabledOidc_RejectsUnsafeFlowConfiguration(string key, string value)
    {
        Dictionary<string, string?> values = Valid();
        values[key] = value;

        Action act = () => OidcConfigurationValidator.Validate(Configuration(values), "Production");

        act.Should().Throw<InvalidOperationException>();
    }

    private static Dictionary<string, string?> Valid() => new()
    {
        ["Oidc:Enabled"] = "true",
        ["Oidc:Authority"] = "https://identity.example.test",
        ["Oidc:ClientId"] = "secureops-ui-test",
        ["Oidc:ClientAuthenticationMethod"] = "None",
        ["Oidc:ApiAudience"] = "secureops-api-test",
        ["Oidc:CallbackPath"] = "/signin-oidc",
        ["Oidc:SignedOutCallbackPath"] = "/signout-callback-oidc",
        ["Oidc:Scopes:0"] = "openid",
        ["Oidc:UsePkce"] = "true",
        ["Oidc:RequireHttpsMetadata"] = "true"
    };

    private static IConfiguration Configuration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
