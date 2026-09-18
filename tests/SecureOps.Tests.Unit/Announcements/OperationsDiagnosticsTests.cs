using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using SecureOps.Infrastructure.Announcements;
using SecureOps.Infrastructure.Announcements.Sources;
using SecureOps.Shared.Contracts.Announcements;

namespace SecureOps.Tests.Unit.Announcements;

public sealed class OperationsDiagnosticsTests
{
    [Fact]
    public async Task EffectiveComposition_UsesBoundDefaultsAndOverrides_WithoutCredentials()
    {
        var defaults = new OperationsDiagnostics(new ConfigurationBuilder().Build());
        var values = new Dictionary<string, string?> { ["Hangfire:SchemaName"] = "HangFire", ["Hangfire:Queue"] = "announcement-source" };
        var explicitDefaults = new OperationsDiagnostics(new ConfigurationBuilder().AddInMemoryCollection(values).Build());
        (await defaults.InspectAsync(default)).ConfigurationFingerprint.Should().Be((await explicitDefaults.InspectAsync(default)).ConfigurationFingerprint);
        values["ConnectionStrings:SecureOpsDb"] = "Server=synthetic;Database=synthetic;User ID=secret-user;Password=secret-password";
        values["TuruncuHat:Password"] = "another-secret";
        values["AnnouncementMail:Password"] = "mail-password";
        values["AnnouncementMail:UserName"] = "mail-private-identity";
        values["AnnouncementMail:Host"] = "relay-private.example.invalid";
        values["AnnouncementMail:SelfTestEnabled"] = "true";
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(values)
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Hangfire:Queue"] = "overridden-queue" }).Build();
        var diagnostic = new OperationsDiagnostics(configuration);
        string json = JsonSerializer.Serialize(diagnostic.Composition());
        json.Should().Contain("overridden-queue").And.NotContain("secret-user").And.NotContain("secret-password").And.NotContain("another-secret");
        json.Should().NotContain("mail-password").And.NotContain("mail-private-identity").And.NotContain("relay-private.example.invalid");
        using var report = JsonDocument.Parse(json);
        report.RootElement.GetProperty("Settings").EnumerateArray().Single(x => x.GetProperty("Key").GetString() == "AnnouncementMail:SelfTestEnabled")
            .GetProperty("Value").GetString().Should().Be("True");
        (await diagnostic.SourceAsync(default)).State.Should().Be("Disabled");
    }

    [Fact]
    public async Task MissingComposition_IsNotDisabled_AndDoesNotContactProviders()
    {
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["Announcements:Enabled"] = "true", ["AnnouncementSource:Enabled"] = "true" }).Build();
        AnnouncementSourceReadiness readiness = await new OperationsDiagnostics(configuration).SourceAsync(default);
        readiness.State.Should().Be("ConfigurationMissing");
        readiness.Missing.Should().Contain("ConnectionStrings:SecureOpsDb").And.Contain("AnnouncementSource:Profiles");
        readiness.WorkerState.Should().Be("NotChecked");
    }

    [Theory]
    [InlineData("17.09.2026 23:59:37", null, null)]
    [InlineData("17.09.2026 23:59:37", "+03:00", "2026-09-17T23:59:37+03:00")]
    [InlineData("2026-09-17T23:59:37.1234567-03:00", "+03:00", "2026-09-17T23:59:37.1234567-03:00")]
    [InlineData("2026-09-17T23:59:37", "+14:01", null)]
    [InlineData("not a date", "+03:00", null)]
    public void SourceTime_RequiresExplicitReviewAndNeverReinterpretsAnInstant(string text, string? offset, string? expected) =>
        SourceWindowEvidence.Resolve(text, offset).Should().Be(expected);
}
