// The test renderer reads the component's own render tree to find its buttons and their event handlers (as bUnit does); the
// repository has no bUnit, and these RenderTree types are only used in this test.
#pragma warning disable BL0006

using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using NSubstitute;
using NSubstitute.Core;
using SecureOps.Ui.Services;
using SecureOps.Ui.Shared.Components;

namespace SecureOps.Tests.Unit.Ui;

/// <summary>
/// The guided tour (GUIDED-TOUR-DESIGN.md): the pure state, and the real SoGuidedTour component driven through its own lifecycle and
/// event handlers by a minimal test renderer with a fake JS module. Synthetic step names only; no browser.
/// </summary>
public sealed class GuidedTourTests
{
    private static readonly SoTourStep[] _steps =
    [
        new("a", "Birinci", "Birinci alan.", "Birinci neden."),
        new("b", "İkinci", "İkinci alan.", "İkinci neden."),
        new("c", "Üçüncü", "Üçüncü alan.", "Üçüncü neden."),
    ];

    [Fact]
    public void State_WalksForwardAndBack_OverStepsOnTheScreen()
    {
        GuidedTourState state = new(_steps);
        state.Start([true, true, true]);

        state.Current!.Target.Should().Be("a");
        state.HasPrevious.Should().BeFalse();
        state.Next([true, true, true]).Should().BeTrue();
        state.Current!.Target.Should().Be("b");
        state.Back([true, true, true]).Should().BeTrue();
        state.Current!.Target.Should().Be("a");
        state.Skipped.Should().BeEmpty();
    }

    [Fact]
    public void State_SkipsAMissingArea_AndSaysWhichOne()
    {
        GuidedTourState state = new(_steps);
        state.Start([false, true, true]);
        state.Current!.Target.Should().Be("b");
        state.Skipped.Select(s => s.Target).Should().Equal("a");

        state.Next([false, false, true]).Should().BeTrue();
        state.Skipped.Should().BeEmpty();

        state.Back([true, false, true]).Should().BeTrue();
        state.Current!.Target.Should().Be("a");
        state.Skipped.Select(s => s.Target).Should().Equal("b");
    }

    [Fact]
    public void State_TellsWhenTheRestIsMissing_AndClosesAfterTheLastShownStep()
    {
        GuidedTourState state = new(_steps);
        state.Start([true, false, false]);

        state.HasNext.Should().BeFalse();
        state.MissingAfter.Select(s => s.Target).Should().Equal("b", "c");
        state.Next([true, false, false]).Should().BeFalse();
        state.IsOpen.Should().BeFalse();
    }

    [Fact]
    public void State_WithNothingOnTheScreen_StaysOpenWithoutAStep_AndAShortAnswerCountsAsMissing()
    {
        GuidedTourState state = new(_steps);
        state.Start([true]);
        state.Current!.Target.Should().Be("a");
        state.HasNext.Should().BeFalse("an answer shorter than the steps means the rest is not on the screen");

        state.Start(null);
        state.IsOpen.Should().BeTrue();
        state.Current.Should().BeNull();
        state.Next([true, true, true]).Should().BeFalse();
    }

    [Fact]
    public void State_DropsStepsTheCallerMayNotTake()
    {
        GuidedTourState state = new([_steps[0], _steps[1] with { Allowed = false }, _steps[2]]);

        state.Steps.Select(s => s.Target).Should().Equal("a", "c");
    }

    [Fact]
    public async Task Component_StartsOnClick_ShowsTheFirstStep_AndAsksTheModuleToHighlightIt()
    {
        await using TourHarness tour = await TourHarness.RenderAsync(_steps, [true, true, true]);

        tour.Find("button", "data-tour-action", "start").Should().NotBeNull();
        tour.Find("div", "data-tour-box").Should().BeNull("the tour never opens on its own");

        await tour.ClickAsync("start");

        tour.Text().Should().Contain("Adım 1 / 3").And.Contain("Birinci").And.Contain("Ne:").And.Contain("Neden:");
        tour.Find("h2", "data-tour-focus").Should().NotBeNull();
        tour.Calls("show").Should().ContainSingle().Which[0].Should().Be("a");
        tour.Calls("present").Single()[0].Should().BeEquivalentTo(new[] { "a", "b", "c" }, "the names travel as one array argument");
        tour.Find("button", "data-tour-action", "back")!.Attributes.Should().ContainKey("disabled");
    }

    [Fact]
    public async Task Component_NextAndBack_MoveBetweenSteps()
    {
        await using TourHarness tour = await TourHarness.RenderAsync(_steps, [true, true, true]);
        await tour.ClickAsync("start");

        await tour.ClickAsync("next");
        tour.Text().Should().Contain("Adım 2 / 3").And.Contain("İkinci");
        await tour.ClickAsync("back");
        tour.Text().Should().Contain("Adım 1 / 3");
        tour.Calls("show").Select(c => c[0]).Should().Equal("a", "b", "a");
    }

    [Fact]
    public async Task Component_SkipsAMissingTarget_AndSaysSoInAStatus()
    {
        await using TourHarness tour = await TourHarness.RenderAsync(_steps, [true, false, true]);
        await tour.ClickAsync("start");

        await tour.ClickAsync("next");

        tour.Text().Should().Contain("Adım 3 / 3").And.Contain("Üçüncü");
        TestElement status = tour.Find("p", "data-tour-skip")!;
        status.Attributes["role"].Should().Be("status");
        status.Text.Should().Contain("Atlanan adım: \"İkinci\"").And.Contain("ekranda yok");
        tour.Find("button", "data-tour-action", "next")!.Text.Should().Be("Bitir");
    }

    [Fact]
    public async Task Component_FoldsTheExplanation_KeepsHeadingAndButtons_AndStaysFoldedOnTheNextStep()
    {
        await using TourHarness tour = await TourHarness.RenderAsync(_steps, [true, true, true]);
        await tour.ClickAsync("start");
        TestElement toggle = tour.Find("button", "data-tour-action", "toggle")!;
        toggle.Text.Should().Be("Açıklamayı gizle");
        toggle.Attributes["aria-expanded"].Should().Be("true");
        toggle.Attributes["aria-controls"].Should().Be("t-body");
        tour.Find("div", "data-tour-body")!.Attributes.Should().NotContainKey("hidden");

        await tour.ClickAsync("toggle");

        tour.Find("button", "data-tour-action", "toggle")!.Text.Should().Be("Açıklamayı göster");
        tour.Find("button", "data-tour-action", "toggle")!.Attributes["aria-expanded"].Should().Be("false");
        tour.Find("div", "data-tour-body")!.Attributes["hidden"].Should().Be(true);
        tour.Text().Should().Contain("Adım 1 / 3").And.Contain("Birinci");
        tour.Find("button", "data-tour-action", "next").Should().NotBeNull();

        await tour.ClickAsync("next");

        tour.Text().Should().Contain("Adım 2 / 3");
        tour.Find("div", "data-tour-body")!.Attributes["hidden"].Should().Be(true, "the fold lasts for the whole tour");
        tour.Calls("show").Select(c => c[0]).Should().Equal("a", "b");
        await tour.ClickAsync("toggle");
        tour.Find("div", "data-tour-body")!.Attributes.Should().NotContainKey("hidden");
    }

    [Fact]
    public async Task Component_Escape_ClosesTheTour_AndReturnsFocusToTheStartButton()
    {
        await using TourHarness tour = await TourHarness.RenderAsync(_steps, [true, true, true]);
        await tour.ClickAsync("start");

        await tour.KeyDownAsync("Escape");

        tour.Find("div", "data-tour-box").Should().BeNull();
        tour.Calls("hide").Should().ContainSingle().Which[0].Should().Be("t-start");
    }

    [Fact]
    public async Task Component_FinishOnTheLastStep_ClosesAndReturnsFocus()
    {
        await using TourHarness tour = await TourHarness.RenderAsync(_steps, [false, false, true]);
        await tour.ClickAsync("start");
        tour.Find("p", "data-tour-skip")!.Text.Should().Contain("\"Birinci\", \"İkinci\"");

        await tour.ClickAsync("next");

        tour.Find("div", "data-tour-box").Should().BeNull();
        tour.Calls("hide").Should().ContainSingle().Which[0].Should().Be("t-start");
    }

    [Fact]
    public async Task Component_HidesAStepTheCallerMayNotTake()
    {
        await using TourHarness tour = await TourHarness.RenderAsync([_steps[0], _steps[1] with { Allowed = false }, _steps[2]], [true, true]);
        await tour.ClickAsync("start");

        tour.Text().Should().Contain("Adım 1 / 2");
        tour.Calls("present").Single()[0].Should().BeEquivalentTo(new[] { "a", "c" });
        await tour.ClickAsync("next");
        tour.Text().Should().Contain("Adım 2 / 2").And.Contain("Üçüncü").And.NotContain("İkinci");
    }

    [Fact]
    public async Task Component_SaysWhenNothingIsOnTheScreen_AndOffersOnlyClose()
    {
        await using TourHarness tour = await TourHarness.RenderAsync(_steps, [false, false, false]);
        await tour.ClickAsync("start");

        tour.Text().Should().Contain("Gösterilecek alan yok").And.NotContain("Adım 1");
        tour.Find("button", "data-tour-action", "next").Should().BeNull();
        tour.Find("button", "data-tour-action", "close").Should().NotBeNull();
        tour.Calls("show").Single()[0].Should().BeNull();
    }

    [Fact]
    public async Task Component_WithoutAllowedSteps_RendersNoButton()
    {
        await using TourHarness tour = await TourHarness.RenderAsync([_steps[0] with { Allowed = false }], []);

        tour.Find("button", "data-tour-action", "start").Should().BeNull();
    }

    /// <summary>Element view of the current render tree: attributes, handlers and text of the subtree.</summary>
    internal sealed record TestElement(string Name, IReadOnlyDictionary<string, object?> Attributes, IReadOnlyDictionary<string, ulong> Handlers, string Text);

    /// <summary>Renders SoGuidedTour with a fake JS module and dispatches its own event handlers.</summary>
    private sealed class TourHarness : IAsyncDisposable
    {
        private readonly TestRenderer _renderer;
        private readonly ServiceProvider _services;
        private readonly IJSObjectReference _module;
        private int _root;

        private TourHarness(ServiceProvider services, IJSObjectReference module)
        {
            _services = services;
            _module = module;
            _renderer = new TestRenderer(services);
        }

        public static async Task<TourHarness> RenderAsync(IReadOnlyList<SoTourStep> steps, bool[] present)
        {
            IJSObjectReference module = Substitute.For<IJSObjectReference>();
            module.InvokeAsync<bool[]?>("present", Arg.Any<object?[]?>()).Returns(new ValueTask<bool[]?>(present));
            IJSRuntime js = Substitute.For<IJSRuntime>();
            js.InvokeAsync<IJSObjectReference>("import", Arg.Any<object?[]?>()).Returns(new ValueTask<IJSObjectReference>(module));
            ServiceCollection registrations = new();
            registrations.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
            registrations.AddSingleton(js);
            TourHarness harness = new(registrations.BuildServiceProvider(), module);
            await harness._renderer.Dispatcher.InvokeAsync(async () =>
            {
                harness._root = harness._renderer.Attach<SoGuidedTour>();
                await harness._renderer.RenderAsync(harness._root, ParameterView.FromDictionary(new Dictionary<string, object?>
                {
                    ["Id"] = "t",
                    ["Title"] = "Deneme",
                    ["Steps"] = steps,
                }));
            });
            return harness;
        }

        public IReadOnlyList<object?[]> Calls(string identifier) => [.. _module.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(IJSObjectReference.InvokeAsync) && Equals(c.GetArguments()[0], identifier))
            .Select(c => c.GetArguments()[^1] as object?[] ?? [])];

        public string Text() => string.Join(" ", Elements().Where(e => e.Name is "p" or "h2" or "button").Select(e => e.Text));

        public TestElement? Find(string name, string attribute, string? value = null) => Elements()
            .FirstOrDefault(e => e.Name == name && e.Attributes.TryGetValue(attribute, out object? v) && (value is null || Equals(v?.ToString(), value)));

        public Task ClickAsync(string action) => DispatchAsync(Find("button", "data-tour-action", action)!, "onclick", new MouseEventArgs());

        public Task KeyDownAsync(string key) => DispatchAsync(Find("div", "data-tour-box")!, "onkeydown", new KeyboardEventArgs { Key = key });

        private Task DispatchAsync(TestElement element, string handler, EventArgs args) =>
            _renderer.Dispatcher.InvokeAsync(() => _renderer.DispatchEventAsync(element.Handlers[handler], null, args));

        private List<TestElement> Elements() => _renderer.Dispatcher.InvokeAsync(() => _renderer.Elements(_root)).GetAwaiter().GetResult();

        public async ValueTask DisposeAsync()
        {
            await _renderer.DisposeAsync();
            await _services.DisposeAsync();
        }
    }

    private sealed class TestRenderer(IServiceProvider services) : Renderer(services, services.GetRequiredService<ILoggerFactory>())
    {
        public override Dispatcher Dispatcher { get; } = Dispatcher.CreateDefault();

        public int Attach<TComponent>() where TComponent : IComponent => AssignRootComponentId(InstantiateComponent(typeof(TComponent)));

        public Task RenderAsync(int id, ParameterView parameters) => RenderRootComponentAsync(id, parameters);

        public List<TestElement> Elements(int componentId)
        {
            List<TestElement> elements = [];
            Collect(componentId, elements);
            return elements;
        }

        private void Collect(int componentId, List<TestElement> elements)
        {
            ArrayRange<RenderTreeFrame> frames = GetCurrentRenderTreeFrames(componentId);
            for (int i = 0; i < frames.Count; i++)
            {
                RenderTreeFrame frame = frames.Array[i];
                if (frame.FrameType == RenderTreeFrameType.Component)
                {
                    Collect(frame.ComponentId, elements);
                }
                else if (frame.FrameType == RenderTreeFrameType.Element)
                {
                    Dictionary<string, object?> attributes = [];
                    Dictionary<string, ulong> handlers = [];
                    StringBuilder text = new();
                    int end = i + frame.ElementSubtreeLength;
                    for (int j = i + 1; j < end; j++)
                    {
                        RenderTreeFrame inner = frames.Array[j];
                        if (inner.FrameType == RenderTreeFrameType.Attribute && j == i + 1 + attributes.Count + handlers.Count)
                        {
                            if (inner.AttributeEventHandlerId != 0)
                            {
                                handlers[inner.AttributeName] = inner.AttributeEventHandlerId;
                            }
                            else
                            {
                                attributes[inner.AttributeName] = inner.AttributeValue;
                            }
                        }
                        else if (inner.FrameType is RenderTreeFrameType.Text)
                        {
                            text.Append(inner.TextContent);
                        }
                        else if (inner.FrameType is RenderTreeFrameType.Markup)
                        {
                            text.Append(inner.MarkupContent);
                        }
                    }

                    elements.Add(new TestElement(frame.ElementName, attributes, handlers, text.ToString().Trim()));
                }
            }
        }

        protected override void HandleException(Exception exception) => throw exception;

        protected override Task UpdateDisplayAsync(in RenderBatch renderBatch) => Task.CompletedTask;
    }
}
