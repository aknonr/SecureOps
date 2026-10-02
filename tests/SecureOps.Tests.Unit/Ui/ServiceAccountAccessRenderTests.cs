using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using MudBlazor.Services;
using NSubstitute;
using SecureOps.Shared.Contracts.Access;
using SecureOps.Ui.Pages.ServiceAccounts;
using SecureOps.Ui.Services;
using SecureOps.Ui.Services.ServiceAccounts;

namespace SecureOps.Tests.Unit.Ui;

public sealed class ServiceAccountAccessRenderTests
{
    [Fact]
    public async Task UnresolvedAccess_RendersActualFailure_NotMissingPermission_AndDoesNotCallModule()
    {
        UiProblem problem = UiProblemFactory.FromResponse(503, new ProblemDetailsPayload { Code = "AuditStoreUnavailable" })
            with
        { Title = "Synthetic access unavailable" };
        (string html, int calls) = await RenderAsync(new AccessSnapshot(null, problem, DateTimeOffset.UnixEpoch));
        html.Should().Contain("Synthetic access unavailable").And.NotContain("Servis hesaplarını görüntüleme yetkisi gerekli");
        calls.Should().Be(0);
    }

    [Fact]
    public async Task ResolvedOrdinaryUser_RendersMissingCapability_AndDoesNotCallModule()
    {
        CurrentAccessResponse access = new(Guid.NewGuid(), "Approved", ["ReadOnly"], [], null, "test",
            new SessionPolicyResponse(15, 8, true, true, "Lax", true, "Synthetic"));
        (string html, int calls) = await RenderAsync(new AccessSnapshot(access, null, DateTimeOffset.UnixEpoch));
        html.Should().Contain("Servis hesaplarını görüntüleme yetkisi gerekli").And.NotContain("Synthetic access unavailable");
        calls.Should().Be(0);
    }

    private static async Task<(string, int)> RenderAsync(AccessSnapshot snapshot)
    {
        ICurrentAccessProvider access = Substitute.For<ICurrentAccessProvider>();
        access.GetAsync(Arg.Any<CancellationToken>()).Returns(snapshot);
        NoModuleCalls handler = new();
        ServiceCollection registrations = new();
        registrations.AddLogging();
        registrations.AddMudServices();
        registrations.AddSingleton(access);
        registrations.AddSingleton(Substitute.For<IJSRuntime>());
        registrations.AddSingleton<NavigationManager>(new SyntheticNavigation());
        registrations.AddSingleton(new ServiceAccountApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://localhost:1/") },
            Substitute.For<IApiSessionContext>()));
        await using ServiceProvider services = registrations.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        string html = await renderer.Dispatcher.InvokeAsync(async () => (await renderer.RenderComponentAsync<ServiceAccountList>()).ToHtmlString());
        return (WebUtility.HtmlDecode(html), handler.Calls);
    }

    private sealed class SyntheticNavigation : NavigationManager
    {
        public SyntheticNavigation() => Initialize("http://localhost/", "http://localhost/service-accounts");
        protected override void NavigateToCore(string uri, bool forceLoad) => throw new InvalidOperationException("Unexpected navigation.");
    }

    private sealed class NoModuleCalls : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            throw new InvalidOperationException("An unresolved or unauthorized caller must not query the module.");
        }
    }
}
