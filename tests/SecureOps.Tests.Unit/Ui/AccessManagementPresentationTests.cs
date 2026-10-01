using System.Net;
using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SecureOps.Infrastructure.Access;
using SecureOps.Shared.Auth;
using SecureOps.Shared.Contracts.Access;
using SecureOps.Shared.Contracts.OperationalRecords;
using SecureOps.Shared.Contracts.ServiceAccounts;
using SecureOps.Ui.Auth;
using SecureOps.Ui.Services;
using SecureOps.Ui.Shared.Components;

namespace SecureOps.Tests.Unit.Ui;

/// <summary>Presentation rules for the access and management screens; synthetic data only.</summary>
public sealed class AccessManagementPresentationTests
{
    private static readonly DateTimeOffset _now = new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ServiceAccountCapabilities_AreNamedGroupedAndScopeQualified()
    {
        foreach (FieldInfo field in typeof(ServiceAccountCapabilities).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            string code = (string)field.GetRawConstantValue()!;
            CapabilityDescriptor descriptor = AccessLabels.Describe(code);
            descriptor.Group.Should().Be(AccessLabels.Groups.ServiceAccounts);
            descriptor.Label.Should().NotBe(code);
            descriptor.Description.Should().NotContain("tanımlı değil");
            AccessLabels.NeedsServiceAccountScope(code).Should().BeTrue();
        }

        AccessLabels.NeedsServiceAccountScope(Capabilities.InUseView).Should().BeFalse();
    }

    [Fact]
    public void CapabilityGroups_FollowFunctionalOrder_AndUnknownSortsLast()
    {
        string[] groups = AccessLabels.Group(
        [
            "Synthetic.Unregistered",
            Capabilities.AccessManageUsers,
            Capabilities.AnnouncementDrafts,
            ServiceAccountCapabilities.View,
            Capabilities.OperationalRecordsView,
            Capabilities.InUseView
        ]).Select(group => group.Key).ToArray();

        groups.Should().Equal(
            AccessLabels.Groups.OperationalRecords,
            AccessLabels.Groups.InUse,
            AccessLabels.Groups.Announcements,
            AccessLabels.Groups.ServiceAccounts,
            AccessLabels.Groups.AccessAdministration,
            AccessLabels.Groups.Other);
    }

    [Fact]
    public void HeldRoles_ShowKnownLabelOrNeutralBusinessRole_NeverAnOpaqueCode()
    {
        AccessLabels.HeldRoleLabel("Operator").Should().Be(AccessLabels.RoleLabel("Operator"));
        AccessLabels.HeldRoleLabel("Business_synthetic01").Should().Be("İş rolü");
    }

    [Fact]
    public void EveryServerCatalogueModule_HasAKnownOrderAndGuidance()
    {
        int unknown = AccessLabels.ModuleOrder("Synthetic module");
        foreach (string module in AccessActionCatalog.Actions.Select(action => action.Module).Distinct())
        {
            AccessLabels.ModuleOrder(module).Should().BeLessThan(unknown, module);
            AccessLabels.ModuleGuidance(module).Should().NotBeNullOrWhiteSpace(module);
        }

        AccessLabels.ModuleGuidance("Synthetic module").Should().BeNull();
    }

    [Theory]
    [InlineData(0, "Bugün geldi", false)]
    [InlineData(3, "3 gündür bekliyor", false)]
    [InlineData(AccessRequestPresentation.LongWaitDays, "7 gündür bekliyor", true)]
    public void PendingRequests_ShowWaitingTimeFromRecordedRequestTime(int days, string text, bool longWait)
    {
        AccessRequestResponse request = Request("Pending", _now.AddDays(-days).AddHours(-1));
        AccessRequestPresentation.WaitingText(request.RequestedAt, _now).Should().Be(text);
        AccessRequestPresentation.IsLongWaiting(request, _now).Should().Be(longWait);
    }

    [Fact]
    public void DecidedRequests_AreNeverLabelledLongWaiting_AndFutureTimesDoNotGoNegative()
    {
        AccessRequestPresentation.IsLongWaiting(Request("Rejected", _now.AddDays(-30)), _now).Should().BeFalse();
        AccessRequestPresentation.IsLongWaiting(Request("Approved", _now.AddDays(-30)), _now).Should().BeFalse();
        AccessRequestPresentation.WaitingDays(_now.AddHours(2), _now).Should().Be(0);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(25, 1)]
    [InlineData(26, 2)]
    [InlineData(251, 11)]
    public void PageCount_UsesTheRequestedPageSize(long total, int pages) =>
        AccessRequestPresentation.PageCount(total).Should().Be(pages);

    [Fact]
    public void SecondaryLine_PrefersAccountThenEmail_AndInventsNothing()
    {
        AccessRequestPresentation.SecondaryLine(null).Should().BeNull();
        AccessRequestPresentation.SecondaryLine(new AccessIdentityProfileResponse(null, null, "synthetic.user@example.invalid", null, null))
            .Should().Be("synthetic.user@example.invalid");
    }

    [Fact]
    public async Task ConfiguredProvider_IsInformational_NotHealthy()
    {
        string html = await RenderCardAsync(new IntegrationProviderHealthResponse("Jira", "Simulation", "Configured"));
        html.Should().Contain("Yapılandırıldı · sınanmadı").And.Contain("Bağlantı ayrıca sınanmadı")
            .And.Contain("so-badge--info").And.NotContain("so-badge--positive").And.NotContain("Sağlıklı");
    }

    [Fact]
    public async Task UnavailableProvider_ExplainsLastCallFailure()
    {
        string html = await RenderCardAsync(new IntegrationProviderHealthResponse("TuruncuHat", "Http", "Unavailable"));
        html.Should().Contain("Son çağrı başarısız").And.Contain("so-badge--critical").And.Contain("Zamanı bildirilmiyor");
    }

    [Fact]
    public async Task UnreadableProvider_RendersUnknown_InsteadOfDisappearing()
    {
        string html = await RenderCardAsync(null);
        html.Should().Contain("Dizin (AD)").And.Contain("Okunamadı").And.Contain("bilinmiyor")
            .And.NotContain("so-badge--positive").And.NotContain("Seçilen sağlayıcı");
    }

    [Fact]
    public async Task UnrecognisedProviderStatus_IsShownVerbatimAndNeutral()
    {
        string html = await RenderCardAsync(new IntegrationProviderHealthResponse("Jira", "Http", "SyntheticFutureState"));
        html.Should().Contain("SyntheticFutureState").And.Contain("so-badge--neutral").And.NotContain("so-badge--positive");
    }

    [Fact]
    public async Task ActionGroups_GroupByCatalogueModule_AndKeepUnknownCodes()
    {
        AccessActionDefinition[] actions =
        [
            new(ServiceAccountCapabilities.View, "Servis Hesapları", "Hesapları gör", "Synthetic"),
            new(Capabilities.InUseView, "In Use", "Kayıtları gör", "Synthetic")
        ];
        string html = await RenderAsync<AccessActionGroups>(new Dictionary<string, object?>
        {
            [nameof(AccessActionGroups.Codes)] = new[] { ServiceAccountCapabilities.View, Capabilities.InUseView, "Synthetic.Unregistered" },
            [nameof(AccessActionGroups.Actions)] = actions
        });
        html.IndexOf("In Use", StringComparison.Ordinal).Should().BeLessThan(html.IndexOf("Servis Hesapları", StringComparison.Ordinal));
        html.Should().Contain("Hesapları gör").And.Contain("Synthetic.Unregistered");

        (await RenderAsync<AccessActionGroups>(new Dictionary<string, object?> { [nameof(AccessActionGroups.Codes)] = Array.Empty<string>() }))
            .Should().Contain("Yok");
    }

    private static AccessRequestResponse Request(string status, DateTimeOffset requestedAt) =>
        new(Guid.NewGuid(), Guid.NewGuid(), "synthetic-identity", status, requestedAt, status == "Pending" ? null : requestedAt.AddHours(1), null);

    private static Task<string> RenderCardAsync(IntegrationProviderHealthResponse? provider) =>
        RenderAsync<SoIntegrationCard>(new Dictionary<string, object?>
        {
            [nameof(SoIntegrationCard.Provider)] = provider,
            [nameof(SoIntegrationCard.Title)] = "Dizin (AD)"
        });

    private static async Task<string> RenderAsync<TComponent>(Dictionary<string, object?> parameters)
        where TComponent : IComponent
    {
        await using ServiceProvider services = new ServiceCollection().AddLogging().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        return WebUtility.HtmlDecode(await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync<TComponent>(ParameterView.FromDictionary(parameters))).ToHtmlString()));
    }
}
