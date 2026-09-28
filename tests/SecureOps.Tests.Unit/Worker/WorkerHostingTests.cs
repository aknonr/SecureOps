using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SecureOps.Infrastructure.Announcements.Sources;
using SecureOps.Worker;
using Xunit;

namespace SecureOps.Tests.Unit.Worker;

public sealed class WorkerHostingTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("secureops-hosting-").FullName;

    [Fact]
    public void Builder_TestOnlyFileUsesExplicitRootAndRetainsCommandLinePrecedence()
    {
        File.WriteAllText(Path.Combine(_root, "appsettings.Test.json"),
            """{"AnnouncementSource:Enabled":true,"Marker":"file"}""");
        HostApplicationBuilder builder = WorkerHosting.CreateBuilder(["--environment", "Test", "--Marker=cli", "--diagnostics"], _root);
        builder.Environment.ContentRootPath.Should().Be(_root);
        builder.Environment.EnvironmentName.Should().Be("Test");
        builder.Configuration["Marker"].Should().Be("cli");
        builder.Configuration.GetValue<bool>("AnnouncementSource:Enabled").Should().BeTrue();
        builder.Configuration["diagnostics"].Should().BeNull();
        builder.Services.Should().NotContain(item => item.ServiceType == typeof(IHostedJobServer));
        File.Exists(Path.Combine(_root, ".secureops-worker.lock")).Should().BeFalse();
        File.Exists(Path.Combine(_root, "worker.log")).Should().BeFalse();
        using IHost host = builder.Build();
    }

    [Fact]
    public void SccmDiagnosticMode_LoadsExplicitInstalledContentRootWithoutAHost()
    {
        File.WriteAllText(Path.Combine(_root, "appsettings.Test.json"), """{"ProbeMarker":"installed"}""");
        HostApplicationBuilder builder = WorkerHosting.CreateBuilder(
            ["--environment", "Test", "--contentRoot", _root, "--sccm-diagnostics", "--SccmDiagnosticProfile", "NonProd"]);
        builder.Configuration["ProbeMarker"].Should().Be("installed");
        builder.Configuration["SccmDiagnosticProfile"].Should().Be("NonProd");
        builder.Configuration["sccm-diagnostics"].Should().BeNull();
        using IHost host = builder.Build();
    }

    [Fact]
    public void Builder_DefaultRootIsBinaryDirectoryNotCurrentDirectory()
    {
        HostApplicationBuilder builder = WorkerHosting.CreateBuilder(["--environment", "Test"]);
        Path.TrimEndingDirectorySeparator(builder.Environment.ContentRootPath)
            .Should().Be(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory));
        using IHost host = builder.Build();
    }

    [Fact]
    public void Builder_RejectsRelativeContentRoot()
    {
        FluentActions.Invoking(() => WorkerHosting.CreateBuilder(["--contentRoot", "relative"]))
            .Should().Throw<InvalidOperationException>().WithMessage("WorkerHosting.AbsoluteContentRootRequired");
    }

    [Theory]
    [InlineData("false", "NonProd", "SccmDiagnostic.ConfigurationRequired")]
    [InlineData("true", "Unapproved", "SccmDiagnostic.ConfigurationRequired")]
    [InlineData("true", "NonProd", "SccmDiagnostic.ProfileRequired")]
    public async Task CollectionDiagnostic_RejectsDisabledUnapprovedOrIncompleteProfilesWithoutDispatch(
        string enabled, string profile, string message)
    {
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AnnouncementSource:Enabled"] = enabled,
            ["AnnouncementSource:CollectionProvider"] = "ConfigurationManager",
            ["SccmDiagnosticProfile"] = profile
        }).Build();
        await FluentActions.Awaiting(() => SccmDiagnosticMode.RunAsync(configuration))
            .Should().ThrowAsync<InvalidOperationException>().WithMessage(message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("relative")]
    [InlineData("\\\\example.invalid\\worker")]
    public void Service_RejectsMissingOrNonlocalDataDirectory(string? directory)
    {
        Func<string?> action = () => WorkerHosting.Validate(Config(directory, "Test"), true);
        action.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Service_RequiresExplicitEnvironmentAndExistingDirectory()
    {
        Func<string?> noEnvironment = () => WorkerHosting.Validate(Config(_root, null), true);
        noEnvironment.Should().Throw<InvalidOperationException>();
        Func<string?> absentDirectory = () => WorkerHosting.Validate(Config(Path.Combine(_root, "absent"), "Test"), true);
        absentDirectory.Should().Throw<InvalidOperationException>();
        WorkerHosting.Validate(Config(_root, "Test"), true).Should().Be(_root);
    }

    [Fact]
    public void Console_WithoutDataDirectoryRetainsCompatibilityWithoutWrites()
    {
        WorkerHosting.Validate(Config(null, null), false).Should().BeNull();
        WorkerHosting.Acquire(null).Should().BeNull();
        WorkerHosting.CreateLifecycleLog(null).Should().BeNull();
    }

    [Fact]
    public void ProcessLock_RejectsDuplicateAndReleasesWithoutDeletingEvidence()
    {
        using (FileStream? first = WorkerHosting.Acquire(_root))
        {
            Func<FileStream?> duplicate = () => WorkerHosting.Acquire(_root);
            duplicate.Should().Throw<IOException>();
        }
        File.Exists(Path.Combine(_root, ".secureops-worker.lock")).Should().BeTrue();
        using FileStream? restart = WorkerHosting.Acquire(_root);
        restart.Should().NotBeNull();
    }

    [Fact]
    public void LifecycleLog_IsStructuredDurableAndReopensAfterDisposal()
    {
        using (Serilog.Core.Logger? log = WorkerHosting.CreateLifecycleLog(_root))
        { log!.Information("Started."); }
        using (Serilog.Core.Logger? log = WorkerHosting.CreateLifecycleLog(_root))
        { log!.Information("Stopped."); }
        string[] records = File.ReadAllLines(Path.Combine(_root, "worker.log"));
        records.Should().HaveCount(2);
        foreach (string record in records)
        {
            using var json = System.Text.Json.JsonDocument.Parse(record);
            json.RootElement.GetProperty("Timestamp").GetDateTimeOffset().Should().BeBefore(DateTimeOffset.UtcNow);
            json.RootElement.TryGetProperty("Exception", out _).Should().BeFalse();
        }
    }

    [Fact]
    public async Task JobLifetime_StopsTheSameServerWithoutStartingAnotherAttempt()
    {
        IHostedJobServer server = Substitute.For<IHostedJobServer>();
        var lifetime = new JobServerHostedService(server, NullLogger<JobServerHostedService>.Instance);
        await lifetime.StartAsync(CancellationToken.None);
        await lifetime.StopAsync(CancellationToken.None);
        server.Received(1).Start();
        server.Received(1).Dispose();
    }

    [Fact]
    public void NativeServiceRegistration_OutsideScmPreservesConsoleLifetime()
    {
        HostApplicationBuilder builder = WorkerHosting.CreateBuilder(["--environment", "Test"], _root);
        builder.Services.AddWindowsService(options => options.ServiceName = WorkerHosting.ServiceName);
        using IHost host = builder.Build();
        host.Services.GetRequiredService<IHostLifetime>().Should().BeOfType<ConsoleLifetime>();
    }

    private static IConfiguration Config(string? directory, string? environment) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["WorkerHosting:DataDirectory"] = directory, [HostDefaults.EnvironmentKey] = environment }).Build();

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
