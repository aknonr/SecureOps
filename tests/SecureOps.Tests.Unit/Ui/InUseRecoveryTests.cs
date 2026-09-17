using System.Collections;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using FluentAssertions;
using Microsoft.JSInterop;
using NSubstitute;
using SecureOps.Infrastructure.Access;
using SecureOps.Shared.Contracts.InUse;
using SecureOps.Ui.Services;
using Workspace = SecureOps.Ui.Pages.InUse;

namespace SecureOps.Tests.Unit.Ui;

public sealed class InUseRecoveryTests
{
    [Fact]
    public async Task ArchivedDownload_JavascriptFailureRetainsArchiveAndRetryDoesNotCreateAnother()
    {
        InUseRecord record = Record();
        var report = new InUseReport(record.Id, record.Version, record.SourceVersion, "synthetic-hash",
            "synthetic.xlsx", [1, 2], [])
        { Archived = true, PreparedBy = Guid.NewGuid(), PreparedAt = DateTimeOffset.UtcNow };
        using var f = new Fixture(_ => Task.FromResult(Response(report)));
        f.SetProperty("Js", new FailedDownload());
        await (Task)f.Invoke("DownloadAsync", (long?)report.Version)!;
        f.Get<InUseReport>("_report").Should().BeEquivalentTo(report);
        f.Get<InUseRecord>("_record").ArchivedVersions.Should().ContainSingle().Which.Should().Be(report.Version);
        f.Get<UiProblem>("_problem").Code.Should().Be("InUseDownloadFailed");
        await (Task)f.Invoke("DownloadAsync", (long?)report.Version)!;
        f.Get<InUseRecord>("_record").ArchivedVersions.Should().ContainSingle().Which.Should().Be(report.Version);
        f.Handler.Calls.Should().Be(2);
    }

    private sealed class FailedDownload : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => throw new JSException("Synthetic download failure");
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => InvokeAsync<TValue>(identifier, args);
    }

    [Theory]
    [InlineData("SaveAsync", "503")]
    [InlineData("SaveAsync", "transport")]
    [InlineData("AssignAsync", "503")]
    [InlineData("AssignAsync", "transport")]
    public async Task RecoverableFailure_RetainsAnswersAndAssignmentIntent(string action, string failure)
    {
        using var f = new Fixture(_ => failure == "transport"
            ? Task.FromException<HttpResponseMessage>(new HttpRequestException())
            : Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        f.Edit();
        await f.Call(action);
        f.Get<string>("_reason").Should().Be("Synthetic reason");
        f.Get<string>("_assignee").Should().Be(f.Assignee);
        f.Answer().Should().Be("Yes");
        f.Get<InUseRecord>("_record").Version.Should().Be(1);
        f.Get<bool>("_dirty").Should().BeTrue();
        f.Get<UiProblem>("_problem").Kind.Should().Be(failure == "503" ? UiProblemKind.UpstreamUnavailable : UiProblemKind.Network);
        f.Handler.Calls.Should().Be(1, "writes are never retried automatically");
    }

    [Fact]
    public async Task AssignmentConflict_ExplicitRecoveryRetainsIntentWithoutOverwritingNewAnswers()
    {
        using var f = new Fixture(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Conflict)));
        f.Edit();
        f.Set("_dirty", false);
        await f.Call("AssignAsync");
        InUseRecord current = f.Record with { Version = 2, Draft = new(1, [new("one", "InternetOut", "No", "Stored history")], "History", Guid.NewGuid(), DateTimeOffset.UtcNow) };
        f.Set("_comparison", current);
        f.Invoke("AcceptComparison");
        f.Get<InUseRecord>("_record").Version.Should().Be(2);
        f.Get<string>("_reason").Should().Be("Synthetic reason");
        f.Get<string>("_assignee").Should().Be(f.Assignee);
        f.Answer().Should().Be("No");
        f.Get<bool>("_dirty").Should().BeFalse("assignment recovery must leave assignment save enabled");
        f.Handler.Calls.Should().Be(1);
    }

    [Fact]
    public async Task SameRecordReload_WithUnsavedEditsOnlyPreparesComparison()
    {
        using var f = new Fixture(_ => Task.FromResult(Response(Record() with { Version = 2 })));
        f.Edit();
        await f.Call("LoadAsync");
        f.Get<InUseRecord>("_record").Version.Should().Be(1);
        f.Get<InUseRecord>("_comparison").Version.Should().Be(2);
        f.Answer().Should().Be("Yes");
        f.Get<string>("_reason").Should().Be("Synthetic reason");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChangedFilter_DiscardsObsoleteSuccessOrFailure(bool fail)
    {
        var pending = new TaskCompletionSource<HttpResponseMessage>();
        using var f = new Fixture(_ => pending.Task);
        f.SetProperty("Id", null);
        Task loading = f.Call("LoadAsync");
        f.Set("_search", "new");
        await f.Call("LoadAsync");
        f.Handler.Next = _ => Task.FromResult(Response(new InUsePage([], 0, 1, 25, InUseRefreshState.Empty)));
        pending.SetResult(fail ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            : Response(new InUsePage([f.Record], 1, 1, 25, InUseRefreshState.Empty)));
        await loading;
        f.Get<InUsePage>("_page").Items.Should().BeEmpty();
        f.Get<UiProblem?>("_problem").Should().BeNull();
        f.Handler.Calls.Should().Be(2);
    }

    [Fact]
    public async Task UnchangedAccessRevalidation_PreservesEdits()
    {
        using var f = new Fixture(_ => throw new InvalidOperationException("No record reload expected"));
        f.Edit();
        await f.Call("RevalidateAccessAsync");
        f.Answer().Should().Be("Yes");
        f.Get<string>("_reason").Should().Be("Synthetic reason");
        f.Get<bool>("_dirty").Should().BeTrue();
    }

    [Fact]
    public async Task AccessRevocation_RejectsDelayedSaveAndClearsProtectedState()
    {
        var pending = new TaskCompletionSource<HttpResponseMessage>();
        using var f = new Fixture(_ => pending.Task);
        f.Edit();
        Task saving = f.Call("SaveAsync");
        f.Access.GetAsync(Arg.Any<CancellationToken>()).Returns(f.Snapshot with
        { Access = f.Snapshot.Access! with { AccessStatus = "Disabled", Capabilities = [], Version = 2 } });
        await f.Call("RevalidateAccessAsync");
        pending.SetResult(Response(f.Record with { Version = 2 }));
        await saving;
        f.Get<InUseRecord?>("_record").Should().BeNull();
        f.Get<string>("_reason").Should().BeEmpty();
        f.Get<InUseRecord?>("_comparison").Should().BeNull();
        f.Get<bool>("_dirty").Should().BeFalse();
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    public async Task DeniedCommand_ClearsProtectedDataAndRequiresAccessRevalidation(int status)
    {
        using var f = new Fixture(_ => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status)));
        f.Edit();
        await f.Call("SaveAsync");
        f.Get<InUseRecord?>("_record").Should().BeNull();
        f.Get<AccessSnapshot>("_access").IsResolved.Should().BeFalse();
        f.Get<string>("_reason").Should().BeEmpty();
    }

    private static InUseRecord Record() => new(Guid.NewGuid(), new("synthetic", "OR-SYNTHETIC", "Synthetic",
        new(null, "Unknown"), new(null, "Unknown"), new(null, "Unknown"),
        [new("one", new Dictionary<string, InUseEvidence>())], "Synthetic", true), "hash", 1, 1, null, null, null, DateTimeOffset.UtcNow);
    private static HttpResponseMessage Response<T>(T value) => new(HttpStatusCode.OK)
    { Content = JsonContent.Create(value, options: ApiResponseReader.JsonOptions) };

    // Exercise the existing component handlers with real HTTP translation, without a new UI dependency.
    private sealed class Fixture : IDisposable
    {
        private const BindingFlags _members = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        private readonly Workspace _component = new();
        private readonly HttpClient _client;
        public InUseRecord Record { get; } = InUseRecoveryTests.Record();
        public string Assignee { get; } = Guid.NewGuid().ToString("D");
        public Handler Handler { get; }
        public ICurrentAccessProvider Access { get; } = Substitute.For<ICurrentAccessProvider>();
        public AccessSnapshot Snapshot { get; } = new(new(Guid.NewGuid(), "Approved", ["Admin"],
            AccessRoleCatalog.GetCapabilities(["Admin"]), null, "Demo", new(30, 12, true, true, "Lax", true, "Local")), null, DateTimeOffset.UtcNow);
        public Fixture(Func<HttpRequestMessage, Task<HttpResponseMessage>> next)
        {
            Handler = new(next);
            _client = new(Handler) { BaseAddress = new("http://localhost/") };
            SetProperty("Api", new InUseApiClient(_client, new FakeApiSessionContext()));
            SetProperty("AccessProvider", Access);
            SetProperty("Id", Record.Id);
            Set("_access", Snapshot);
            Access.GetAsync(Arg.Any<CancellationToken>()).Returns(Snapshot);
            Invoke("SetRecord", Record);
        }
        public void Edit()
        {
            Set("_reason", "Synthetic reason");
            Set("_assignee", Assignee);
            object answer = ((IEnumerable)Get<object>("_answers")).Cast<object>().First();
            answer.GetType().GetProperty("Value")!.SetValue(answer, "Yes");
            Invoke("Dirty");
        }
        public string Answer() => (string)((IEnumerable)Get<object>("_answers")).Cast<object>().First().GetType()
            .GetProperty("Value")!.GetValue(((IEnumerable)Get<object>("_answers")).Cast<object>().First())!;
        public T Get<T>(string name) => (T)typeof(Workspace).GetField(name, _members)!.GetValue(_component)!;
        public void Set(string name, object? value) => typeof(Workspace).GetField(name, _members)!.SetValue(_component, value);
        public void SetProperty(string name, object? value) => typeof(Workspace).GetProperty(name, _members)!.SetValue(_component, value);
        public object? Invoke(string name, params object[] args) => typeof(Workspace).GetMethod(name, _members)!.Invoke(_component, args);
        public Task Call(string name) => (Task)Invoke(name)!;
        public void Dispose() { _component.Dispose(); _client.Dispose(); }
    }
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> next) : HttpMessageHandler
    {
        public Func<HttpRequestMessage, Task<HttpResponseMessage>> Next { get; set; } = next;
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { Calls++; return Next(request); }
    }
}
