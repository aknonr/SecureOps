using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SecureOps.Domain.Resources;
using SecureOps.Ui.Services;
using SecureOps.Ui.Shared.Components;

namespace SecureOps.Tests.Unit.Ui;

public sealed class ResourceWorkspaceTests
{
    [Fact]
    public void Selection_AllMeansOnlyCurrentPageInItsOrder()
    {
        var state = new ResourceSelectionState();
        Guid[] page = [Guid.NewGuid(), Guid.NewGuid()];
        state.SetPage(page);
        state.Toggle(Guid.NewGuid(), true);
        state.Ids.Should().BeEmpty();
        state.SelectPage(true);
        state.All.Should().BeTrue();
        state.Ids.Should().Equal(page);
        state.Toggle(page[0], false);
        state.All.Should().BeFalse();
        state.Ids.Should().Equal(page[1]);
        state.SetPage([Guid.NewGuid()]);
        state.Ids.Should().BeEmpty();
        state.SelectPage(true);
        state.Clear();
        state.Ids.Should().BeEmpty();
        state.All.Should().BeFalse();
    }

    [Fact]
    public void Selection_BoundsAndDeduplicatesPageScope()
    {
        var state = new ResourceSelectionState();
        Guid[] page = Enumerable.Range(0, 101).Select(_ => Guid.NewGuid()).ToArray();
        state.SetPage(page.Concat(page));
        state.SelectPage(true);
        state.Ids.Should().Equal(page.Take(100));
        state.SelectPage(false);
        state.Ids.Should().BeEmpty();
    }

    [Fact]
    public void Shortcuts_UnknownRoutesHaveNoTargetAndUnresolvedAccessHasNoPermission()
    {
        ResourceWorkspaceView.Href("admin/arbitrary").Should().BeNull();
        ResourceWorkspaceView.Permitted(null, "catalogue").Should().BeFalse();
        ResourceWorkspaceView.Keys.Should().OnlyContain(key => ResourceWorkspaceView.Href(key) != null);
        ResourceView.PageSizes.Should().Equal(10, 25, 50, 100);
    }

    [Fact]
    public async Task ResolvedOpening_IsANativeButtonWithIsolatedIndividualFallback()
    {
        await using ServiceProvider services = new ServiceCollection().AddLogging().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        var link = new ResourceLink(Guid.NewGuid(), Guid.NewGuid(), "Synthetic", "https://example.invalid/only", "Synthetic",
            null, null, null, [], 0, true, false, 1, DateTimeOffset.UnixEpoch);
        string html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            Microsoft.AspNetCore.Components.Web.HtmlRendering.HtmlRootComponent result = await renderer.RenderComponentAsync<ResolvedResourceLinks>(ParameterView.FromDictionary(new Dictionary<string, object?>
            { [nameof(ResolvedResourceLinks.Links)] = new[] { link } }));
            return System.Net.WebUtility.HtmlDecode(result.ToHtmlString());
        });
        html.Should().Contain("data-so-open-links").And.NotContain("data-so-open-choice").And.Contain("data-so-open-url");
        html.Should().Contain("target=\"_blank\"").And.Contain("rel=\"noopener noreferrer\"");
        html.Should().Contain("anlamına gelmez").And.NotContain("onclick=");
    }

    [Fact]
    public void Resolution_ContextChangeRejectsLateSuccessAndErrors()
    {
        var state = new ResourceResolutionState<string[]>();
        long stale = state.Clear();
        long current = state.Clear();
        state.Accept(stale, ["old"]).Should().BeFalse();
        state.IsCurrent(stale).Should().BeFalse();
        state.Value.Should().BeNull();
        state.Accept(current, ["current"]).Should().BeTrue();
        state.Clear();
        state.Value.Should().BeNull();
        state.Accept(current, ["late"]).Should().BeFalse();
    }

    [Theory]
    [InlineData(409, true)]
    [InlineData(503, false)]
    [InlineData(401, false)]
    public async Task GroupRecovery_ExplicitReadOnlyRefreshNeverReplaysSave(int status, bool allowed)
    {
        var state = new ResourceSaveState();
        await state.TrySaveAsync(() => Task.FromResult<UiProblem?>(UiProblemFactory.FromResponse(status, null)));
        int reads = 0;
        await state.RefreshConflictAsync(() => { reads++; return Task.CompletedTask; });
        reads.Should().Be(allowed ? 1 : 0);
        state.CanSubmit.Should().Be(allowed);
    }

    [Fact]
    public async Task GroupRecovery_FailedRereadKeepsSavingBlocked()
    {
        var state = new ResourceSaveState();
        await state.TrySaveAsync(() => Task.FromResult<UiProblem?>(UiProblemFactory.FromResponse(409, null)));
        await state.RefreshConflictAsync(() => throw new SecureOpsApiException(UiProblemFactory.FromResponse(503, null)));
        state.CanSubmit.Should().BeFalse();
        state.Problem!.StatusCode.Should().Be(503);
    }
}
