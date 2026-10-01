using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SecureOps.Api.Security;
using SecureOps.Shared.Configuration;

namespace SecureOps.Tests.Integration.Api;

public sealed class PersistentDataProtectionTests
{
    [WindowsFact]
    public void PersistentKeyRing_SurvivesProviderRecreationForSessionAndDirectoryTokens()
    {
        string keyRingPath = TemporaryKeyRing();
        try
        {
            IConfiguration configuration = Configuration(keyRingPath, "SecureOps.Api");
            using ServiceProvider firstProvider = Services(configuration);
            ManualTimeProvider time = new(new DateTimeOffset(2026, 8, 23, 12, 0, 0, TimeSpan.Zero));
            DataProtectedDirectoryContinuationTokenCodec firstCodec = Codec(firstProvider, time);
            ApplicationSessionCookie firstCookie = Cookie(firstProvider);
            string continuation = firstCodec.Create("group-direct-members", "ops-read", 25);
            var sessionId = Guid.NewGuid();
            var firstContext = new DefaultHttpContext();
            firstCookie.Write(firstContext.Response, sessionId);
            string protectedCookie = CookieValue(firstContext.Response);

            using ServiceProvider recreatedProvider = Services(configuration);
            DataProtectedDirectoryContinuationTokenCodec recreatedCodec = Codec(recreatedProvider, time);
            var recreatedContext = new DefaultHttpContext();
            recreatedContext.Request.Headers.Cookie = $"__Host-SecureOps.ApplicationSession={protectedCookie}";

            recreatedCodec.TryRead(continuation, "group-direct-members", "ops-read", out int offset).Should().BeTrue();
            offset.Should().Be(25);
            Cookie(recreatedProvider).Read(recreatedContext.Request).Should().Be(
                new ApplicationSessionCookieReadResult(ApplicationSessionCookieStatus.Valid, sessionId));
            Directory.GetFiles(keyRingPath, "*.xml").Should().NotBeEmpty();
        }
        finally
        {
            Directory.Delete(keyRingPath, recursive: true);
        }
    }

    [WindowsFact]
    public void ContinuationToken_IsApplicationPurposeTargetOperationExpiryAndIntegrityBound()
    {
        string keyRingPath = TemporaryKeyRing();
        try
        {
            ManualTimeProvider time = new(new DateTimeOffset(2026, 8, 23, 12, 0, 0, TimeSpan.Zero));
            using ServiceProvider provider = Services(Configuration(keyRingPath, "SecureOps.Api"));
            DataProtectedDirectoryContinuationTokenCodec codec = Codec(provider, time);
            string token = codec.Create("members", "ops-read", 25);

            codec.TryRead(token, "groups", "ops-read", out _).Should().BeFalse();
            codec.TryRead(token, "members", "other", out _).Should().BeFalse();
            codec.TryRead(token + "x", "members", "ops-read", out _).Should().BeFalse();
            token.Should().NotContain("ops-read");
            Action wrongPurpose = () => provider.GetRequiredService<IDataProtectionProvider>()
                .CreateProtector("SecureOps.WrongPurpose")
                .Unprotect(token);
            wrongPurpose.Should().Throw<CryptographicException>();

            using ServiceProvider wrongApplication = Services(Configuration(keyRingPath, "SecureOps.OtherApplication"));
            Codec(wrongApplication, time).TryRead(token, "members", "ops-read", out _).Should().BeFalse();
            time.Advance(TimeSpan.FromSeconds(31));
            codec.TryRead(token, "members", "ops-read", out _).Should().BeFalse();
        }
        finally
        {
            Directory.Delete(keyRingPath, recursive: true);
        }
    }

    [Theory]
    [InlineData("Pilot")]
    [InlineData("Production")]
    public void Validation_RejectsEphemeralModeForControlledEnvironments(string environment)
    {
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DataProtection:Mode"] = "Ephemeral",
            ["DataProtection:ApplicationName"] = "SecureOps.Api"
        }).Build();

        Action act = () => DataProtectionConfiguration.Validate(configuration, environment);

        act.Should().Throw<InvalidOperationException>().WithMessage("*persistent Data Protection*");
    }

    [Fact]
    public void Validation_RejectsRelativePersistentKeyRingPath()
    {
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DataProtection:Mode"] = "FileSystemDpapi",
            ["DataProtection:ApplicationName"] = "SecureOps.Api",
            ["DataProtection:KeyRingPath"] = "relative-key-ring"
        }).Build();

        Action act = () => DataProtectionConfiguration.Validate(configuration, "Pilot");

        act.Should().Throw<InvalidOperationException>().WithMessage("*absolute server-owned path*");
    }

    private static IConfiguration Configuration(string keyRingPath, string applicationName) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DataProtection:Mode"] = "FileSystemDpapi",
            ["DataProtection:ApplicationName"] = applicationName,
            ["DataProtection:KeyRingPath"] = keyRingPath
        }).Build();

    private static ServiceProvider Services(IConfiguration configuration)
    {
        DataProtectionConfiguration.Validate(configuration, "Pilot");
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSecureOpsDataProtection(configuration);
        return services.BuildServiceProvider();
    }

    private static DataProtectedDirectoryContinuationTokenCodec Codec(IServiceProvider services, TimeProvider time) => new(
        services.GetRequiredService<IDataProtectionProvider>(),
        Options.Create(new DirectoryExplorerOptions { ContinuationTokenLifetimeSeconds = 30 }),
        time);

    private static ApplicationSessionCookie Cookie(IServiceProvider services) => new(
        services.GetRequiredService<IDataProtectionProvider>(),
        Options.Create(new SessionSecurityOptions()));

    private static string CookieValue(HttpResponse response)
    {
        string setCookie = response.Headers.SetCookie.Single()!;
        return setCookie[(setCookie.IndexOf('=') + 1)..setCookie.IndexOf(';')];
    }

    private static string TemporaryKeyRing()
    {
        string path = Path.Combine(Path.GetTempPath(), "secureops-dp-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now = _now.Add(duration);
    }
}
