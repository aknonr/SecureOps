using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SecureOps.Domain.OperationalRecords;
using SecureOps.Infrastructure.Access;
using SecureOps.Shared.Contracts.OperationalRecords;
using SecureOps.Ui.Pages;
using SecureOps.Ui.Services;

namespace SecureOps.Tests.Unit.Ui;

public sealed class SdmRecoveryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ObsoleteRead_CannotReplaceNewRecordOrError(bool fail)
    {
        using var f = new Fixture();
        var pending = new TaskCompletionSource<OperationalRecordResponse>();
        f.Records.GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(pending.Task);
        Task old = f.Call("LoadAsync", false);
        OperationalRecordResponse next = f.Record with { Id = Guid.NewGuid(), Version = 2 };
        f.Records.GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(next);
        await f.Call("LoadAsync", false);
        if (fail)
        {
            pending.SetException(new SecureOpsApiException(Failure()));
        }
        else
        {
            pending.SetResult(f.Record);
        }
        await old;
        f.Get<OperationalRecordResponse>("_record").Should().Be(next);
        f.Get<UiProblem?>("_loadProblem").Should().BeNull();
    }

    [Theory]
    [InlineData(UiProblemKind.UpstreamUnavailable)]
    [InlineData(UiProblemKind.Network)]
    public async Task ReviewFailure_RetainsDeclarationWithoutRepeatingCommand(UiProblemKind kind)
    {
        using var f = new Fixture();
        f.Set("_declaredType", OperationalRecordClassification.SoftwareInstallation);
        f.Records.ReviewAsync(Arg.Any<Guid>(), Arg.Any<JiraReviewRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<JiraPreviewResponse>(new SecureOpsApiException(Failure() with { Kind = kind })));
        await f.Call("ReviewAsync");
        f.Get<OperationalRecordClassification>("_declaredType").Should().Be(OperationalRecordClassification.SoftwareInstallation);
        f.Get<JiraPreviewResponse?>("_preview").Should().BeNull();
        f.Get<UiProblem>("_actionProblem").Kind.Should().Be(kind);
        await f.Records.Received(1).ReviewAsync(Arg.Any<Guid>(), Arg.Any<JiraReviewRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Revocation_DiscardsDelayedReviewAndProtectedState()
    {
        using var f = new Fixture();
        f.Set("_declaredType", OperationalRecordClassification.ServerRequest);
        var pending = new TaskCompletionSource<JiraPreviewResponse>();
        f.Records.ReviewAsync(Arg.Any<Guid>(), Arg.Any<JiraReviewRequest>(), Arg.Any<CancellationToken>()).Returns(pending.Task);
        Task reviewing = f.Call("ReviewAsync");
        f.Access.GetAsync(Arg.Any<CancellationToken>()).Returns(f.Snapshot with
        { Access = f.Snapshot.Access! with { AccessStatus = "Disabled", Capabilities = [], Version = 2 } });
        await f.Call("RevalidateAccessAsync");
        pending.SetResult(JsonSerializer.Deserialize<JiraPreviewResponse>("""{"warnings":[],"blockingConditions":[]}""", ApiResponseReader.JsonOptions)!);
        await reviewing;
        f.Get<OperationalRecordResponse?>("_record").Should().BeNull();
        f.Get<JiraPreviewResponse?>("_preview").Should().BeNull();
        f.Get<OperationalRecordClassification?>("_declaredType").Should().BeNull();
        f.Get<AccessSnapshot>("_access").Access!.AccessStatus.Should().Be("Disabled");
        await f.Records.DidNotReceive().CreateJiraAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SameAccess_PreservesDeclaration_AndParameterChangeReloadsExactRecord()
    {
        using var f = new Fixture();
        f.Set("_declaredType", OperationalRecordClassification.ServerRetirement);
        await f.Call("RevalidateAccessAsync");
        f.Get<OperationalRecordClassification>("_declaredType").Should().Be(OperationalRecordClassification.ServerRetirement);
        var next = Guid.NewGuid();
        f.SetProperty("RecordId", next);
        await f.Call("OnParametersSetAsync");
        await f.Records.Received(1).GetAsync(next, Arg.Any<CancellationToken>());
        f.Get<OperationalRecordClassification?>("_declaredType").Should().BeNull();
    }

    private static UiProblem Failure() => UiProblemFactory.NetworkFailure();

    private sealed class Fixture : IDisposable
    {
        private const BindingFlags _members = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        private readonly EmptyDetail _component = new();
        private readonly TestRenderer _renderer = new();
        public IOperationalRecordApiClient Records { get; } = Substitute.For<IOperationalRecordApiClient>();
        public ICurrentAccessProvider Access { get; } = Substitute.For<ICurrentAccessProvider>();
        public OperationalRecordResponse Record { get; } = JsonSerializer.Deserialize<OperationalRecordResponse>("""
            {"id":"00000000-0000-0000-0000-000000000123","sourceRecordId":"123","orCode":"OR-123",
             "title":"Synthetic","description":"Synthetic","version":1,"reasonCodes":[],"blockingConditions":[]}
            """, ApiResponseReader.JsonOptions)!;
        public AccessSnapshot Snapshot { get; } = new(new(Guid.NewGuid(), "Approved", ["Admin"],
            AccessRoleCatalog.GetCapabilities(["Admin"]), null, "Demo", new(30, 12, true, true, "Lax", true, "Local")), null, DateTimeOffset.UtcNow);
        public Fixture()
        {
            SetProperty("Records", Records);
            SetProperty("AccessProvider", Access);
            SetProperty("RecordId", Record.Id);
            Set("_record", Record);
            Set("_access", Snapshot);
            Access.GetAsync(Arg.Any<CancellationToken>()).Returns(Snapshot);
            Records.GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Record);
            _renderer.Attach(_component);
        }
        public T Get<T>(string name) => (T)typeof(OperationalRecordDetail).GetField(name, _members)!.GetValue(_component)!;
        public void Set(string name, object? value) => typeof(OperationalRecordDetail).GetField(name, _members)!.SetValue(_component, value);
        public void SetProperty(string name, object? value) => typeof(OperationalRecordDetail).GetProperty(name, _members)!.SetValue(_component, value);
        public Task Call(string name, params object[] args) => _renderer.Dispatcher.InvokeAsync(() =>
            (Task)typeof(OperationalRecordDetail).GetMethod(name, _members)!.Invoke(_component, args)!);
        public void Dispose() { _component.Dispose(); ((IDisposable)_renderer).Dispose(); }
    }

    private sealed class EmptyDetail : OperationalRecordDetail
    {
        protected override void BuildRenderTree(Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder builder) { }
    }

#pragma warning disable BL0006 // Test-only renderer attaches the actual component handlers without UI packages.
    private sealed class TestRenderer() : Renderer(new ServiceCollection().BuildServiceProvider(), NullLoggerFactory.Instance)
    {
        public override Dispatcher Dispatcher { get; } = Dispatcher.CreateDefault();
        public void Attach(IComponent component) => AssignRootComponentId(component);
        protected override void HandleException(Exception exception) => throw new InvalidOperationException("Test render failed", exception);
        protected override Task UpdateDisplayAsync(in RenderBatch renderBatch) => Task.CompletedTask;
    }
#pragma warning restore BL0006
}
