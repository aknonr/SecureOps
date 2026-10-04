using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using MudBlazor.Services;
using NSubstitute;
using SecureOps.Shared.Auth;
using SecureOps.Shared.Contracts.Access;
using SecureOps.Shared.Contracts.ServiceAccounts;
using SecureOps.Ui.Pages;
using SecureOps.Ui.Services;

namespace SecureOps.Tests.Unit.Ui;

/// <summary>
/// The Service-Accounts-only landing (owner decision 2026-10-04), exercised through the real Dashboard component
/// lifecycle with the static HTML renderer: an approved user whose only entry is the module is sent from "/" to
/// /service-accounts (replace); "/dashboard", any other entry, a management view or a pending user keep the board.
/// Synthetic access snapshots only; no API, browser or identity provider.
/// </summary>
public sealed class ServiceAccountLandingRenderTests
{
    [Fact]
    public async Task ServiceAccountsOnlyUser_IsSentFromRootToTheModule_WithReplace()
    {
        RecordingNavigation navigation = await RenderAsync("http://localhost/", "Approved", ServiceAccountCapabilities.View, ServiceAccountCapabilities.Work);
        navigation.Navigations.Should().ContainSingle().Which.Should().Be(("http://localhost/service-accounts", true));
    }

    [Theory]
    [InlineData("http://localhost/dashboard", "Approved", new[] { ServiceAccountCapabilities.View })]
    [InlineData("http://localhost/", "Approved", new[] { ServiceAccountCapabilities.View, Capabilities.IdentityLookup })]
    [InlineData("http://localhost/", "Pending", new[] { ServiceAccountCapabilities.View })]
    [InlineData("http://localhost/", "Approved", new string[0])]
    public async Task EveryoneElse_StaysOnTheBoard(string uri, string status, string[] capabilities)
    {
        RecordingNavigation navigation = await RenderAsync(uri, status, capabilities);
        navigation.Navigations.Should().BeEmpty();
    }

    [Fact]
    public void ManagementViewers_KeepTheBoard_EvenWhenTheModuleIsTheirOnlyOtherEntry()
    {
        CurrentAccessResponse access = new(Guid.NewGuid(), "Approved", ["Synthetic"], [ServiceAccountCapabilities.View, Capabilities.ManagementReportingView], null,
            "test", new SessionPolicyResponse(15, 8, true, true, "Lax", true, "Synthetic"));
        AccessSnapshot snapshot = new(access, null, DateTimeOffset.UnixEpoch);
        IReadOnlyList<Dashboard.OperatorTask> tasks = Dashboard.BuildTasks(snapshot);

        tasks.Select(t => t.Route).Should().Contain("service-accounts");
        Dashboard.OnlyServiceAccounts(snapshot, tasks, managementView: true).Should().BeFalse();
        Dashboard.OnlyServiceAccounts(snapshot, [.. tasks.Where(t => t.Route == "service-accounts")], managementView: false).Should().BeTrue();
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("?tab=1", true)]
    [InlineData("#top", true)]
    [InlineData("dashboard", false)]
    [InlineData("service-accounts", false)]
    public void RootDetection_IgnoresQueryAndFragmentOnly(string relative, bool root) => Dashboard.IsRoot(relative).Should().Be(root);

    private static async Task<RecordingNavigation> RenderAsync(string uri, string status, params string[] capabilities)
    {
        CurrentAccessResponse access = new(Guid.NewGuid(), status, ["Synthetic"], capabilities, null, "test",
            new SessionPolicyResponse(15, 8, true, true, "Lax", true, "Synthetic"));
        AccessSnapshot snapshot = new(access, null, DateTimeOffset.UnixEpoch);
        ICurrentAccessProvider provider = Substitute.For<ICurrentAccessProvider>();
        provider.GetAsync(Arg.Any<CancellationToken>()).Returns(snapshot);
        provider.RefreshAsync(Arg.Any<CancellationToken>()).Returns(snapshot);
        RecordingNavigation navigation = new(uri);

        ServiceCollection registrations = new();
        registrations.AddLogging();
        registrations.AddMudServices();
        registrations.AddSingleton(provider);
        registrations.AddSingleton(Substitute.For<ISignedInUserService>());
        registrations.AddSingleton(Substitute.For<IIdentityLookupApiClient>());
        registrations.AddSingleton(Substitute.For<IManagementReportingApiClient>());
        registrations.AddSingleton(Substitute.For<IJSRuntime>());
        registrations.AddSingleton<NavigationManager>(navigation);
        await using ServiceProvider services = registrations.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () => await renderer.RenderComponentAsync<Dashboard>());
        return navigation;
    }

    private sealed class RecordingNavigation : NavigationManager
    {
        public RecordingNavigation(string uri) => Initialize("http://localhost/", uri);

        public List<(string Uri, bool Replace)> Navigations { get; } = [];

        protected override void NavigateToCore(string uri, NavigationOptions options) => Navigations.Add((ToAbsoluteUri(uri).ToString(), options.ReplaceHistoryEntry));
    }
}
