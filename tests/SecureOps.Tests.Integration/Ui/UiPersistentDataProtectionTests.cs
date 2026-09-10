using FluentAssertions;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SecureOps.Ui.Hosting;

namespace SecureOps.Tests.Integration.Ui;

public sealed class UiPersistentDataProtectionTests
{
    [Theory]
    [InlineData("Ephemeral", false, false)]
    [InlineData("FileSystemDpapi", false, true)]
    [InlineData("FileSystemDpapi", true, false)]
    public async Task Antiforgery_RestartRequiresPersistentKeysAndStableApplication(string mode, bool differentApplication, bool valid)
    {
        string path = Path.Combine(Path.GetTempPath(), $"secureops-csrf-{Guid.NewGuid():N}");
        WebApplication Host(string application)
        {
            WebApplicationBuilder builder = WebApplication.CreateBuilder();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DataProtection:Mode"] = mode,
                ["DataProtection:ApplicationName"] = application,
                ["DataProtection:KeyRingPath"] = path
            });
            builder.Services.AddSecureOpsUiDataProtection(builder.Configuration);
            builder.Services.AddAntiforgery(o => { o.HeaderName = "X-Synthetic-Csrf"; o.Cookie.Name = "__Host-SecureOpsUi.AntiForgery"; o.Cookie.SecurePolicy = CookieSecurePolicy.Always; });
            return builder.Build();
        }
        try
        {
            await using WebApplication first = Host("Synthetic.Ui");
            var issuedContext = new DefaultHttpContext { RequestServices = first.Services };
            issuedContext.Request.Scheme = "https";
            AntiforgeryTokenSet tokens = first.Services.GetRequiredService<IAntiforgery>().GetAndStoreTokens(issuedContext);
            await using WebApplication restarted = Host(differentApplication ? "Synthetic.Other" : "Synthetic.Ui");
            var request = new DefaultHttpContext { RequestServices = restarted.Services };
            request.Request.Scheme = "https";
            request.Request.Method = "POST";
            request.Request.Headers.Cookie = $"__Host-SecureOpsUi.AntiForgery={tokens.CookieToken}";
            request.Request.Headers["X-Synthetic-Csrf"] = tokens.RequestToken;
            Func<Task> validate = () => restarted.Services.GetRequiredService<IAntiforgery>().ValidateRequestAsync(request);
            if (valid)
            {
                await validate.Should().NotThrowAsync();
            }
            else
            {
                await validate.Should().ThrowAsync<AntiforgeryValidationException>();
            }
        }
        finally
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }
    }

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
