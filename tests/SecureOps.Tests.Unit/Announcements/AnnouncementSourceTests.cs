using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using SecureOps.Domain.Announcements;
using SecureOps.Infrastructure.Announcements.Sources;
using SecureOps.Infrastructure.OperationalRecords;
using SecureOps.Shared.Configuration;

namespace SecureOps.Tests.Unit.Announcements;

public sealed class AnnouncementSourceTests
{
    [Theory]
    [InlineData("14.09.2026 01:02:03", "Unresolved")]
    [InlineData("2026-09-14T01:02:03", "Unresolved")]
    [InlineData("2026-09-14T01:02:03.1234567", "Unresolved")]
    [InlineData("2026-09-14T01:02:03.123+03:00", "Resolved")]
    [InlineData("2026-09-14T01:02:03Z", "Resolved")]
    [InlineData(" 2026-09-14T01:02+03:00 ", "Resolved")]
    [InlineData(null, "Missing")]
    [InlineData("  ", "Missing")]
    [InlineData("not.a.date", "Invalid")]
    [InlineData("2026-02-30T01:02Z", "Invalid")]
    public void Window_PreservesEveryOriginalCharacterAndClassifiesEvidence(string? text, string resolution)
    {
        ChangeWindowResult result = SourceWindowEvidence.Read(text, text);
        result.StartText.Should().Be(text);
        result.FinishText.Should().Be(text);
        result.Resolution.Should().Be(resolution);
    }

    [Fact]
    public void Window_RejectsOversizeAndDoesNotResolveIncompleteOrReversedPairs()
    {
        FluentActions.Invoking(() => SourceWindowEvidence.Read(new string('x', 129), null))
            .Should().Throw<AnnouncementSourceException>();
        SourceWindowEvidence.Read("2026-09-14T01:02Z", null).Resolution.Should().Be("Unresolved");
        SourceWindowEvidence.Read("2026-09-14T02:00Z", "2026-09-14T01:00Z").Resolution.Should().Be("Invalid");
    }

    [Fact]
    public void Recipients_PreserveManualAddsRemovalsAndDeduplicateAcrossProfileSwitches()
    {
        ReconciledRecipients first = RecipientReconciler.Reconcile(
            [" manual@example.invalid ", "a@example.invalid", "A@example.invalid"],
            ["a@example.invalid", "b@example.invalid"], [], [], []);
        ReconciledRecipients second = RecipientReconciler.Reconcile(
            ["manual@example.invalid", "a@example.invalid"], ["b@example.invalid", "c@example.invalid"],
            ["a@example.invalid", "b@example.invalid"], first.Manual, first.Removed);
        second.Difference.Proposed.Should().Equal("c@example.invalid", "manual@example.invalid", "a@example.invalid");
        second.Difference.PreservedRemoval.Should().Equal("b@example.invalid");
        ReconciledRecipients third = RecipientReconciler.Reconcile(second.Difference.Proposed,
            ["b@example.invalid", "a@example.invalid"], ["b@example.invalid", "c@example.invalid"], second.Manual, second.Removed);
        third.Difference.Proposed.Should().NotContain("b@example.invalid").And.Contain("manual@example.invalid");
        AnnouncementContent content = new("", "", "", "", "", "", "", "", "", "", [], [], "");
        RecipientReconciler.WithRecipients(content, ["a@example.invalid"], ["A@example.invalid", "c@example.invalid"])
            .Cc.Should().Equal("c@example.invalid");
    }

    [Fact]
    public void Profiles_RequireCompleteConfigurationAndNeverExpandAllowlist()
    {
        var settings = new AnnouncementSourceOptions();
        settings.Profiles["Untrusted"] = Profile();
        settings.Profiles["NonProd"] = Profile();
        var catalog = new MaintenanceProfileCatalog(Options.Create(settings));
        catalog.Choices().Should().HaveCount(5).And.NotContain(p => p.Name == "Untrusted");
        catalog.Resolve("NonProd").State.Should().Be("Configured");
        catalog.Resolve("nonprod").State.Should().Be("Invalid");
        settings.Profiles["NonProd"].To = [];
        catalog.Resolve("NonProd").Missing.Should().Contain("To");
        catalog.Resolve("Prod01").State.Should().Be("Unconfigured");
    }

    [Fact]
    public async Task Collector_RetainsAll205ServicesAndDeviceProvenanceWithoutInferringDraftDates()
    {
        (ICollectionMembershipClient collections, IAnnouncementServiceSourceClient services) = Clients(205);
        services.GetDeviceServicesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => new ServiceLookupResult(call.Arg<string>(), ["service-" + call.Arg<string>()], "Resolved"));
        AnnouncementSourceSnapshot snapshot = await Collector(collections, services).CollectAsync(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "NonProd", "SYNTHETIC", "OCO-TEST", default);
        snapshot.Services.Should().HaveCount(205).And.OnlyContain(s => s.Devices.Length == 1);
        snapshot.Completeness.Partial.Should().BeFalse();
        snapshot.Work!.ProposedStartDate.Should().BeNull();
        snapshot.Work.ProposedStartText.Should().Be("2026-09-14T01:00:00.123+03:00");
    }

    [Theory]
    [InlineData("Missing", 1, 0, 0)]
    [InlineData("Failed", 0, 1, 0)]
    [InlineData("Ambiguous", 0, 0, 1)]
    public async Task Collector_MissingFailedAndAmbiguousAreExplicitPartialResults(string resolution, int missing, int failed, int ambiguous)
    {
        (ICollectionMembershipClient collections, IAnnouncementServiceSourceClient services) = Clients(1);
        services.GetDeviceServicesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ServiceLookupResult("device-0", resolution == "Ambiguous" ? ["a", "b"] : [], resolution));
        AnnouncementSourceSnapshot snapshot = await Collector(collections, services).CollectAsync(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "NonProd", "SYNTHETIC", "OCO-TEST", default);
        snapshot.Completeness.Partial.Should().BeTrue();
        snapshot.Completeness.ServicesMissing.Should().Be(missing);
        snapshot.Completeness.ServicesFailed.Should().Be(failed);
        snapshot.Completeness.ServicesAmbiguous.Should().Be(ambiguous);
        if (ambiguous > 0)
        { snapshot.Services.Select(s => s.Name).Should().Equal("a", "b"); }
    }

    [Fact]
    public async Task Collector_EmptyCollectionAndMissingWindowNeverBecomeComplete()
    {
        (ICollectionMembershipClient collections, IAnnouncementServiceSourceClient services) = Clients(0);
        services.GetChangeWindowAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new ChangeWindowResult(null, null, "Missing"));
        AnnouncementSourceSnapshot result = await Collector(collections, services).CollectAsync(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "NonProd", "SYNTHETIC", "OCO-TEST", default);
        result.Completeness.Partial.Should().BeTrue();
        result.Completeness.Warnings.Should().Contain("CollectionHadNoDevices").And.Contain("ChangeWindowMissing");
    }

    [Theory]
    [InlineData("SET.service", 1, "Resolved")]
    [InlineData("KEY.service", 1, "Failed")]
    [InlineData("SET.service", 2, "Ambiguous")]
    public async Task Adapter_RequiresExactKeysAndNeverClaimsUnfetchedPagesAreComplete(string key, int pages, string resolution)
    {
        using var handler = new ResponseHandler(JsonSerializer.Serialize(new
        {
            QueryResult = new
            {
                MaxPages = pages,
                PageNO = 1,
                Items = new[] { new[] {
                new { Key = "num", Value = "1" }, new { Key = key, Value = "service-a" } } }
            }
        }));
        ServiceLookupResult result = await Adapter(handler).GetDeviceServicesAsync("device-1", default);
        result.Resolution.Should().Be(resolution);
        handler.Requests.Should().Be(1);
    }

    [Fact]
    public async Task Collector_BoundsConcurrentLookupsAndHonorsCancellation()
    {
        (ICollectionMembershipClient collections, IAnnouncementServiceSourceClient services) = Clients(10);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0, active = 0;
        services.GetDeviceServicesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(async call =>
        {
            Interlocked.Increment(ref active);
            if (Interlocked.Increment(ref calls) == 2)
            { started.SetResult(); }
            try
            { await Task.Delay(Timeout.Infinite, call.Arg<CancellationToken>()); }
            finally { Interlocked.Decrement(ref active); }
            return new ServiceLookupResult(call.Arg<string>(), [], "Missing");
        });
        var collector = new AnnouncementSourceCollector(collections, services,
            Options.Create(new AnnouncementSourceOptions { ServiceLookupConcurrency = 2 }), TimeProvider.System,
            NullLogger<AnnouncementSourceCollector>.Instance);
        using var cancellation = new CancellationTokenSource();
        Task<AnnouncementSourceSnapshot> pending = collector.CollectAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "NonProd", "SYNTHETIC", "OCO-TEST", cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
        calls.Should().Be(2);
        cancellation.Cancel();
        await FluentActions.Awaiting(() => pending).Should().ThrowAsync<OperationCanceledException>();
        active.Should().Be(0);
    }

    [Fact]
    public async Task Adapter_PreservesDottedDatesAndRejectsConflictingDuplicateKeys()
    {
        using var dates = new ResponseHandler("""
            {"QueryResult":{"Items":[[{"Key":"SET.p_proposed_finish_date_time","Value":"14.09.2026 02:00"},
            {"Key":"SET.p_proposed_start_date_time","Value":"14.09.2026 01:00"}]]}}
            """);
        ChangeWindowResult result = await Adapter(dates).GetChangeWindowAsync("OCO-TEST", default);
        result.StartText.Should().Be("14.09.2026 01:00");
        result.Resolution.Should().Be("Unresolved");
        using var duplicate = new ResponseHandler("""
            {"QueryResult":{"Items":[[{"Key":"SET.service","Value":"a"},{"Key":"SET.service","Value":"b"}]]}}
            """);
        (await Adapter(duplicate).GetDeviceServicesAsync("device-1", default)).Resolution.Should().Be("Failed");
        using var repeated = new ResponseHandler("""
            {"QueryResult":{"Items":[
            [{"Key":"SET.p_proposed_start_date_time","Value":"2026-09-14T01:00Z"},{"Key":"SET.p_proposed_finish_date_time","Value":"2026-09-14T02:00Z"}],
            [{"Key":"SET.p_proposed_start_date_time","Value":"2026-09-14T01:00Z"},{"Key":"SET.p_proposed_finish_date_time","Value":"2026-09-14T02:00Z"}]]}}
            """);
        (await Adapter(repeated).GetChangeWindowAsync("OCO-TEST", default)).Resolution.Should().Be("Ambiguous");
    }

    private static MaintenanceProfileOptions Profile() => new()
    { CollectionId = "SYNTHETIC", Scope = "Scope", Impact = "Impact", Checks = "Checks", Description = "Description", To = ["a@example.invalid"] };

    private static (ICollectionMembershipClient, IAnnouncementServiceSourceClient) Clients(int count)
    {
        ICollectionMembershipClient collections = Substitute.For<ICollectionMembershipClient>();
        IAnnouncementServiceSourceClient services = Substitute.For<IAnnouncementServiceSourceClient>();
        collections.GetDevicesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new CollectionMembershipResult(
            Enumerable.Range(0, count).Select(i => new SourceDevice("device-" + i, "SYNTHETIC", DateTimeOffset.UtcNow)).ToArray(), true, 2, []));
        services.GetChangeWindowAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(
            SourceWindowEvidence.Read("2026-09-14T01:00:00.123+03:00", "2026-09-14T02:00:00.123+03:00"));
        return (collections, services);
    }

    private static AnnouncementSourceCollector Collector(ICollectionMembershipClient collections, IAnnouncementServiceSourceClient services) =>
        new(collections, services, Options.Create(new AnnouncementSourceOptions()), TimeProvider.System, NullLogger<AnnouncementSourceCollector>.Instance);

    private static TuruncuHatAnnouncementSourceClient Adapter(ResponseHandler handler)
    {
        ITuruncuHatSessionManager sessions = Substitute.For<ITuruncuHatSessionManager>();
        sessions.GetSessionAsync(Arg.Any<CancellationToken>()).Returns("synthetic-session");
        return new(new HttpClient(handler) { BaseAddress = new Uri("https://source.invalid/") }, sessions,
            Options.Create(new TuruncuHatOptions()), Options.Create(new AnnouncementSourceOptions
            { ServiceInstanceBaseObject = "Synthetic", ServiceNameSelect = "service", ChangeBaseObject = "SyntheticChange" }),
            NullLogger<TuruncuHatAnnouncementSourceClient>.Instance);
    }

    private sealed class ResponseHandler(string body) : HttpMessageHandler
    {
        public int Requests { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }
}
