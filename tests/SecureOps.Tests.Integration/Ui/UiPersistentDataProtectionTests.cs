using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SecureOps.Ui.Hosting;

namespace SecureOps.Tests.Integration.Ui;

public sealed class UiPersistentDataProtectionTests
{
    [Fact]
    public void PersistentKeyRing_SurvivesUiProviderRecreation()
    {
        string keyRingPath = Path.Combine(Path.GetTempPath(), $"secureops-ui-dp-{Guid.NewGuid():N}");
        IConfiguration configuration = Configuration(keyRingPath);
        try
        {
            using ServiceProvider firstServices = Services(configuration);
            IDataProtector first = firstServices.GetRequiredService<IDataProtectionProvider>()
                .CreateProtector("SecureOps.Ui.CookieRegression.v1");
            string protectedValue = first.Protect("ui-cookie-payload");

            using ServiceProvider secondServices = Services(configuration);
            IDataProtector second = secondServices.GetRequiredService<IDataProtectionProvider>()
                .CreateProtector("SecureOps.Ui.CookieRegression.v1");

            second.Unprotect(protectedValue).Should().Be("ui-cookie-payload");
        }
        finally
        {
            if (Directory.Exists(keyRingPath))
            {
                Directory.Delete(keyRingPath, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData("Pilot")]
    [InlineData("Production")]
    public void Validation_RejectsEphemeralModeForControlledUiEnvironments(string environmentName)
    {
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DataProtection:Mode"] = "Ephemeral",
            ["DataProtection:ApplicationName"] = "SecureOps.Ui"
        }).Build();

        Action act = () => UiDataProtectionConfiguration.Validate(configuration, environmentName);

        act.Should().Throw<InvalidOperationException>().WithMessage("*persistent Data Protection*");
    }

    private static IConfiguration Configuration(string keyRingPath) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DataProtection:Mode"] = "FileSystemDpapi",
            ["DataProtection:ApplicationName"] = "SecureOps.Ui",
            ["DataProtection:KeyRingPath"] = keyRingPath
        }).Build();

    private static ServiceProvider Services(IConfiguration configuration)
    {
        UiDataProtectionConfiguration.Validate(configuration, "Pilot");
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSecureOpsUiDataProtection(configuration);
        return services.BuildServiceProvider();
    }
}
