using FluentAssertions;
using SecureOps.Domain.Resources;
using SecureOps.Shared.Contracts.Resources;
using SecureOps.Ui.Services;

namespace SecureOps.Tests.Unit.Ui;

public sealed class ResourceExperienceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Query_IgnoresOlderSuccessOrFailureEvenWhenTransportIgnoresCancellation(bool oldFails)
    {
        using var state = new ResourceQueryState();
        var older = new TaskCompletionSource<ResourcePage>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken oldToken = default;
        Task first = state.LoadAsync(new(Search: "old"), (_, token) => { oldToken = token; return older.Task; });
        state.Loading.Should().BeTrue();
        var current = new ResourcePage([], 2, 25, 26);
        await state.LoadAsync(new(Search: "new"), (_, _) => Task.FromResult(current));
        oldToken.IsCancellationRequested.Should().BeTrue();
        if (oldFails)
        {
            older.SetException(new SecureOpsApiException(UiProblemFactory.FromResponse(503, null)));
        }
        else
        {
            older.SetResult(new([], 1, 25, 0));
        }
        await first;
        state.Page.Should().BeSameAs(current);
        state.Problem.Should().BeNull();
        state.Loading.Should().BeFalse();
    }

    [Fact]
    public async Task Query_FailureRetainsDataAndExplicitReloadRecovers()
    {
        using var state = new ResourceQueryState();
        var initial = new ResourcePage([], 1, 25, 0);
        await state.LoadAsync(new(), (_, _) => Task.FromResult(initial));
        await state.LoadAsync(new(), (_, _) => Task.FromException<ResourcePage>(new SecureOpsApiException(UiProblemFactory.FromResponse(503, null))));
        state.Page.Should().BeSameAs(initial);
        state.Problem!.StatusCode.Should().Be(503);
        await state.LoadAsync(new(), (_, _) => Task.FromResult(initial));
        state.Problem.Should().BeNull();
    }

    [Fact]
    public async Task Query_DisposalCancelsAndSuppressesLatePublication()
    {
        var state = new ResourceQueryState();
        var response = new TaskCompletionSource<ResourcePage>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken observed = default;
        Task pending = state.LoadAsync(new(), (_, token) => { observed = token; return response.Task; });
        state.Dispose();
        observed.IsCancellationRequested.Should().BeTrue();
        response.SetResult(new([], 1, 25, 0));
        await pending;
        state.Page.Should().BeNull();
    }

    [Fact]
    public async Task Save_DoubleSubmissionIsRejectedAndValidationKeepsTheDraftEditable()
    {
        var state = new ResourceSaveState();
        var response = new TaskCompletionSource<UiProblem?>(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0;
        Task<UiProblem?> Save()
        { calls++; return response.Task; }
        Task<bool> pending = state.TrySaveAsync(Save);
        state.Saving.Should().BeTrue();
        (await state.TrySaveAsync(Save)).Should().BeFalse();
        calls.Should().Be(1);
        response.SetResult(UiProblemFactory.FromResponse(400, null));
        (await pending).Should().BeFalse();
        state.CanSubmit.Should().BeTrue();
        (await state.TrySaveAsync(() => Task.FromResult<UiProblem?>(null))).Should().BeTrue();
    }

    [Theory]
    [InlineData(409)]
    [InlineData(503)]
    [InlineData(401)]
    public async Task Save_ConflictOutageOrExpiryNeverAutoRetriesOrClosesTheDraft(int status)
    {
        var state = new ResourceSaveState();
        int calls = 0;
        Task<UiProblem?> Save()
        { calls++; throw new SecureOpsApiException(UiProblemFactory.FromResponse(status, null)); }
        (await state.TrySaveAsync(Save)).Should().BeFalse();
        (await state.TrySaveAsync(Save)).Should().BeFalse();
        calls.Should().Be(1);
        state.Saving.Should().BeFalse();
        state.CanSubmit.Should().BeFalse();
        state.Problem!.StatusCode.Should().Be(status);
    }

    [Fact]
    public void FavouriteFiltering_UsesAllPermittedPreferencesAndPagesAfterFiltering()
    {
        var category = Guid.NewGuid();
        ResourceLink Link(int i) => new(Guid.NewGuid(), category, $"Synthetic {i:D2}", "https://example.invalid/", "Purpose", null,
            i == 26 ? "Pilot" : "Lab", null, ["example"], i, true, false, 1, DateTimeOffset.UnixEpoch);
        ResourceLink[] links = [.. Enumerable.Range(0, 28).Select(Link)];
        var preferences = new ResourcePreferencesResponse(1, links, [], null);
        ResourceView.FavouritePage(preferences, new(Environment: "pilot", PageSize: 25))!.Items.Should().Equal(links[26]);
        ResourceView.FavouritePage(preferences, new(Page: 2, PageSize: 25))!.Items.Should().Equal(links.Skip(25));
        ResourceView.FavouritePage(preferences, new(Search: "example", CategoryId: category))!.Total.Should().Be(28);
        ResourceView.FavouritePage(preferences, new(CategoryId: Guid.NewGuid()))!.Total.Should().Be(0);
    }

    [Theory]
    [InlineData("links")]
    [InlineData("groups")]
    [InlineData("management")]
    public void Guide_ContainsOnlyAuthorizedWorkingRoutesAndNoLegacyTerminology(string surface)
    {
        IReadOnlyList<ResourceGuideStep> ordinary = ResourceGuideSteps.For(surface, false);
        ordinary.Should().NotBeEmpty().And.OnlyContain(step => step.Href == "resources" || step.Href == "resources/sets");
        string copy = string.Join(" ", ordinary.Select(s => s.Title + s.Description));
        copy.Should().NotContain("vardiya").And.NotContain("set ").And.NotContain("Mesai");
        ResourceGuideSteps.For(surface, true).Should().Contain(step => step.Href == "admin/resources");
    }
}
